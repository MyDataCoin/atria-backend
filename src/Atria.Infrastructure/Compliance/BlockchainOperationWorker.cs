using System.Globalization;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Domain.Compliance;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Signer;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Background worker that drains queued <see cref="BlockchainOperation"/> records:
/// builds a <see cref="SigningRequest"/>, submits via <see cref="IBlockchainSigner"/>
/// and tracks status. A reconciliation pass marks submitted operations confirmed.
/// Idempotent: a status guard ensures an operation is never sent twice.
/// </summary>
public sealed class BlockchainOperationWorker : BackgroundService
{
    private readonly int _maxAttempts;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;
    private readonly int _confirmationsRequired;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlockchainOperationWorker> _logger;
    private readonly TokenSigningOptions _signing;
    private readonly string? _agentKey;

    public BlockchainOperationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BlockchainOptions> options,
        IOptions<TokenSigningOptions> signing,
        IOptions<EvmAnchorOptions> anchor,
        ILogger<BlockchainOperationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _signing = signing.Value;
        _agentKey = anchor.Value.AgentPrivateKey;

        var opts = options.Value;
        _maxAttempts = opts.MaxAttempts;
        _batchSize = opts.BatchSize;
        _pollInterval = TimeSpan.FromSeconds(opts.PollSeconds);
        _confirmationsRequired = opts.ConfirmationsRequired;
    }

    /// <summary>
    /// Reports, once, which addresses this process will actually sign as.
    /// </summary>
    /// <remarks>
    /// A key that belongs to an address the contract granted no role produces a reverted transaction
    /// and nothing else — the most common way a deployment is wrong, and the slowest to recognise
    /// from the receipt. Printing the derived addresses at start makes it a comparison against the
    /// role table rather than a debugging session. Addresses are public; the keys never leave here.
    /// </remarks>
    private void LogSigningIdentities()
    {
        _logger.LogInformation(
            "Token signing mode: {Mode}. Minter {Minter}, oracle {Oracle}, allowlist agent {Agent}. "
            + "These must be the addresses the token contract granted MINTER_ROLE and ORACLE_ROLE to, "
            + "and the address registered as an allowlist agent.",
            _signing.Mode,
            AddressOf(_signing.MinterPrivateKey),
            AddressOf(_signing.OraclePrivateKey),
            AddressOf(_agentKey));
    }

    private static string AddressOf(string? privateKey)
    {
        if (string.IsNullOrWhiteSpace(privateKey))
            return "(not configured)";

        try
        {
            return new EthECKey(privateKey).GetPublicAddress();
        }
        catch (Exception)
        {
            return "(unreadable key)";
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogSigningIdentities();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A fresh scope per iteration so scoped services (DbContext) are not reused.
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
                var signer = scope.ServiceProvider.GetRequiredService<IBlockchainSigner>();
                var networks = scope.ServiceProvider.GetRequiredService<IChainNetworkResolver>();
                var allowlist = scope.ServiceProvider.GetRequiredService<IAllowlistGateway>();
                var tokens = scope.ServiceProvider.GetRequiredService<ITokenGateway>();

                await SubmitPendingAsync(context, signer, networks, allowlist, tokens, stoppingToken);
                var receipts = scope.ServiceProvider.GetRequiredService<IChainReceiptReader>();

                await ReconcileSubmittedAsync(context, networks, receipts, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blockchain operation worker iteration failed.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Issues the shares of an activated application to the investor's address.
    /// </summary>
    /// <remarks>
    /// The contract enforces the order: the address must already be on the allowlist, and a mint to
    /// an unlisted one reverts. That is why AllowlistAdd is queued before TokenAllocation and why a
    /// revert here is surfaced as a failure rather than swallowed — a share counted in the registry
    /// but never minted is exactly the discrepancy the reconciliation exists to prevent.
    /// </remarks>
    private static async Task<string> MintAsync(
        ITokenGateway tokens, BlockchainOperation operation,
        (string ChainId, string? TokenContractAddress) target, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(operation.Payload);
        var root = payload.RootElement;

        var wallet = root.TryGetProperty("wallet", out var w) ? w.GetString() : null;
        // Whole shares: a fractional payload is a bug upstream, and TryGetInt64 refusing it here is
        // the point — reading it as a decimal and truncating would mint a number nobody authorised.
        var tokenCount = root.TryGetProperty("tokenCount", out var c) && c.TryGetInt64(out var parsed)
            ? parsed
            : 0L;
        var chain = root.TryGetProperty("chain", out var ch) ? ch.GetString() : null;

        if (string.IsNullOrWhiteSpace(wallet) || tokenCount <= 0)
            throw new InvalidOperationException(
                $"Operation {operation.Id} (TokenAllocation) needs a wallet and a positive token count.");

        if (string.IsNullOrWhiteSpace(target.TokenContractAddress))
            throw new InvalidOperationException(
                $"Operation {operation.Id} targets an issue with no token contract recorded; deploy it first.");

        if (string.IsNullOrWhiteSpace(chain))
            throw new InvalidOperationException(
                $"Operation {operation.Id} (TokenAllocation) carries no chain tag.");

        var result = await tokens.MintAsync(chain, target.TokenContractAddress, wallet, tokenCount, ct);
        return result.TransactionRef;
    }

    /// <summary>Delivers the recorded collateral attestation into the issue's contract (§16).</summary>
    private static async Task<string> ReportCollateralAsync(
        ITokenGateway tokens, BlockchainOperation operation,
        (string ChainId, string? TokenContractAddress) target, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(operation.Payload);
        var root = payload.RootElement;

        var chain = root.TryGetProperty("chain", out var ch) ? ch.GetString() : null;
        var hashHex = root.TryGetProperty("dataHash", out var h) ? h.GetString() : null;
        var valuation = root.TryGetProperty("valuation", out var v) ? v.GetDecimal() : 0m;
        var valuedAt = root.TryGetProperty("valuedAtUtc", out var d) ? d.GetDateTime() : default;
        var uri = root.TryGetProperty("uri", out var u) ? u.GetString() ?? string.Empty : string.Empty;

        if (string.IsNullOrWhiteSpace(chain) || string.IsNullOrWhiteSpace(hashHex)
            || string.IsNullOrWhiteSpace(target.TokenContractAddress))
        {
            throw new InvalidOperationException(
                $"Operation {operation.Id} (CollateralReport) is missing the chain, the hash or the contract.");
        }

        var result = await tokens.ReportCollateralAsync(
            chain, target.TokenContractAddress, Convert.FromHexString(hashHex), valuation, valuedAt, uri, ct);

        return result.TransactionRef;
    }

    /// <summary>
    /// Applies an allowlist decision on chain. The gateway writes and waits for the receipt, so by
    /// the time it returns the address really is (or is not) permitted; there is no separate
    /// submission reference to reconcile later.
    /// </summary>
    private static async Task<string> WriteAllowlistAsync(
        IAllowlistGateway allowlist, BlockchainOperation operation, CancellationToken ct)
    {
        using var payload = JsonDocument.Parse(operation.Payload);
        var root = payload.RootElement;

        var chain = root.TryGetProperty("chain", out var c) ? c.GetString() : null;
        var address = root.TryGetProperty("walletAddress", out var w) ? w.GetString() : null;

        if (string.IsNullOrWhiteSpace(chain) || string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException(
                $"Operation {operation.Id} ({operation.Type}) needs both a chain and a wallet address.");

        if (operation.Type == BlockchainOperationType.AllowlistAdd)
            await allowlist.AddAsync(chain, address, ct);
        else
            await allowlist.RemoveAsync(chain, address, ct);

        return $"allowlist:{chain}:{address}";
    }

    /// <summary>
    /// The network and token contract an operation targets, taken from the issue named in its
    /// payload. Operations against shared contracts (allowlist, identity registry) carry no token
    /// address; they still need a network, which comes from the issue that triggered them.
    /// </summary>
    private static async Task<(string ChainId, string? TokenContractAddress)> ResolveTargetAsync(
        AtriaDbContext context, IChainNetworkResolver networks, BlockchainOperation operation,
        CancellationToken ct)
    {
        Guid? propertyId = null;
        try
        {
            using var payload = JsonDocument.Parse(operation.Payload);
            if (payload.RootElement.TryGetProperty("propertyId", out var value)
                && value.TryGetGuid(out var parsed))
            {
                propertyId = parsed;
            }
        }
        catch (JsonException)
        {
            // Malformed payload: fall through and fail below with a message that says why.
        }

        if (propertyId is null)
            throw new InvalidOperationException(
                $"Operation {operation.Id} ({operation.Type}) carries no propertyId, so its network cannot be resolved.");

        var property = await context.Set<Property>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct)
            ?? throw new InvalidOperationException(
                $"Operation {operation.Id} references property {propertyId} which no longer exists.");

        var network = networks.Resolve(property.TokenChain)
            ?? throw new InvalidOperationException(
                $"Issue {property.Id} is on chain '{property.TokenChain}', which is not configured under Blockchain:Networks.");

        return (network.ChainId.ToString(CultureInfo.InvariantCulture), property.TokenContractAddress);
    }

    /// <summary>Submits operations still in Created/Failed (under the attempt cap) to the signer.</summary>
    private async Task SubmitPendingAsync(
        AtriaDbContext context, IBlockchainSigner signer, IChainNetworkResolver networks,
        IAllowlistGateway allowlist, ITokenGateway tokens, CancellationToken ct)
    {
        var pending = await context.Set<BlockchainOperation>()
            .Where(o => (o.Status == BlockchainOperationStatus.Created
                         || o.Status == BlockchainOperationStatus.Failed)
                        && o.Attempts < _maxAttempts)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var operation in pending)
        {
            // Status guard: skip anything that is no longer submittable (idempotency).
            if (operation.Status is BlockchainOperationStatus.Submitted or BlockchainOperationStatus.Confirmed)
                continue;

            operation.IncrementAttempt();

            try
            {
                string txRef;

                if (operation.Type is BlockchainOperationType.AllowlistAdd
                    or BlockchainOperationType.AllowlistRemove)
                {
                    // Allowlist writes go straight to the restriction contract through the gateway,
                    // signed by the scoped registry/allowlist agent. They never reach the custody
                    // signer: that key exists for token operations, and the allowlist is not one.
                    txRef = await WriteAllowlistAsync(allowlist, operation, ct);
                }
                else if (operation.Type == BlockchainOperationType.TokenAllocation)
                {
                    var target = await ResolveTargetAsync(context, networks, operation, ct);
                    txRef = await MintAsync(tokens, operation, target, ct);
                }
                else if (operation.Type == BlockchainOperationType.CollateralReport)
                {
                    var target = await ResolveTargetAsync(context, networks, operation, ct);
                    txRef = await ReportCollateralAsync(tokens, operation, target, ct);
                }
                else
                {
                    // Which network and which contract come from the issue the operation belongs to.
                    // Reading them from a global setting is how every issue ends up minting into one
                    // contract the moment a second issue exists.
                    var target = await ResolveTargetAsync(context, networks, operation, ct);

                    var request = new SigningRequest(
                        OperationType: operation.Type.ToString(),
                        UnsignedPayload: operation.Payload,
                        ChainId: target.ChainId,
                        TokenContractAddress: target.TokenContractAddress);

                    var result = await signer.SignAndSubmitAsync(request, ct);
                    txRef = result.SubmissionReference ?? result.SignedPayload;
                }

                operation.MarkSubmitted(txRef);

                _logger.LogInformation(
                    "Submitted blockchain operation {OperationId} ({Type}); tx {TransactionRef}.",
                    operation.Id, operation.Type, txRef);
            }
            catch (Exception ex)
            {
                operation.MarkFailed(ex.Message);
                _logger.LogWarning(ex,
                    "Blockchain operation {OperationId} ({Type}) failed on attempt {Attempts}.",
                    operation.Id, operation.Type, operation.Attempts);
            }
        }

        if (pending.Count > 0)
            await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Reconciliation pass: asks the chain what happened to each submitted operation and settles it
    /// only on the answer.
    /// </summary>
    /// <remarks>
    /// Three outcomes, and none of them is assumed. No receipt means not mined yet — leave it
    /// submitted and ask again. A reverted receipt means the transaction consumed gas and changed
    /// nothing, so the operation failed and must be retried or investigated, never counted. A
    /// successful receipt is only confirmed once enough blocks sit on top of it: a single-block
    /// confirmation can be undone by a reorg, and an allocation reported as settled and then
    /// vanishing is the discrepancy the holder registry can least afford.
    /// </remarks>
    private async Task ReconcileSubmittedAsync(
        AtriaDbContext context, IChainNetworkResolver networks, IChainReceiptReader receipts,
        CancellationToken ct)
    {
        var submitted = await context.Set<BlockchainOperation>()
            .Where(o => o.Status == BlockchainOperationStatus.Submitted)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_batchSize)
            .ToListAsync(ct);

        var changed = false;

        foreach (var operation in submitted)
        {
            if (string.IsNullOrWhiteSpace(operation.TransactionRef))
                continue;

            // Allowlist writes are applied synchronously by the gateway, which already waited for
            // the receipt; their reference is not a transaction hash and there is nothing to poll.
            if (operation.Type is BlockchainOperationType.AllowlistAdd
                or BlockchainOperationType.AllowlistRemove)
            {
                operation.MarkConfirmed();
                changed = true;
                continue;
            }

            string? chain;
            try
            {
                chain = ChainTagOf(operation);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot determine the network of operation {OperationId}.", operation.Id);
                continue;
            }

            ChainReceipt? receipt;
            try
            {
                receipt = await receipts.GetReceiptAsync(chain, operation.TransactionRef, ct);
            }
            catch (Exception ex)
            {
                // A node that is down is not evidence about the transaction. Leave it submitted.
                _logger.LogWarning(
                    ex, "Could not read the receipt for operation {OperationId} (tx {TxRef}); will retry.",
                    operation.Id, operation.TransactionRef);
                continue;
            }

            if (receipt is null)
                continue;

            if (!receipt.Succeeded)
            {
                operation.MarkFailed($"Transaction {operation.TransactionRef} reverted in block {receipt.BlockNumber}.");
                await ApplyToInvestmentAsync(context, operation, OnChainStatus.Failed, ct);
                changed = true;

                _logger.LogError(
                    "Blockchain operation {OperationId} ({Type}) reverted on chain; tx {TxRef}.",
                    operation.Id, operation.Type, operation.TransactionRef);
                continue;
            }

            if (receipt.Confirmations < _confirmationsRequired)
            {
                _logger.LogDebug(
                    "Operation {OperationId} has {Confirmations}/{Required} confirmations.",
                    operation.Id, receipt.Confirmations, _confirmationsRequired);
                continue;
            }

            operation.MarkConfirmed();
            await ApplyToInvestmentAsync(context, operation, OnChainStatus.Confirmed, ct);
            changed = true;

            _logger.LogInformation(
                "Confirmed blockchain operation {OperationId} ({Type}) with {Confirmations} confirmation(s); tx {TxRef}.",
                operation.Id, operation.Type, receipt.Confirmations, operation.TransactionRef);
        }

        if (changed)
            await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Writes the on-chain outcome back onto the investment an allocation belongs to, so the
    /// investor sees the real state of their allocation rather than the platform's intent.
    /// </summary>
    private async Task ApplyToInvestmentAsync(
        AtriaDbContext context, BlockchainOperation operation, OnChainStatus status, CancellationToken ct)
    {
        if (operation.Type != BlockchainOperationType.TokenAllocation)
            return;

        Guid? investmentId = null;
        try
        {
            using var payload = JsonDocument.Parse(operation.Payload);
            if (payload.RootElement.TryGetProperty("investmentId", out var value)
                && value.TryGetGuid(out var parsed))
            {
                investmentId = parsed;
            }
        }
        catch (JsonException)
        {
            // Handled below by the null check.
        }

        if (investmentId is null)
        {
            _logger.LogWarning(
                "Allocation {OperationId} carries no investmentId; the investment keeps its previous state.",
                operation.Id);
            return;
        }

        var investment = await context.Set<Investment>()
            .FirstOrDefaultAsync(i => i.Id == investmentId, ct);

        if (investment is null)
        {
            _logger.LogWarning(
                "Allocation {OperationId} references investment {InvestmentId}, which no longer exists.",
                operation.Id, investmentId);
            return;
        }

        investment.SetOnChainResult(operation.TransactionRef!, status);
    }

    /// <summary>The chain tag an operation targets, taken from its payload.</summary>
    private static string ChainTagOf(BlockchainOperation operation)
    {
        using var payload = JsonDocument.Parse(operation.Payload);
        var chain = payload.RootElement.TryGetProperty("chain", out var value) ? value.GetString() : null;

        return string.IsNullOrWhiteSpace(chain)
            ? throw new InvalidOperationException(
                $"Operation {operation.Id} ({operation.Type}) carries no chain tag.")
            : chain;
    }
}
