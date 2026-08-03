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
    private readonly bool _autoConfirmSubmitted;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlockchainOperationWorker> _logger;

    public BlockchainOperationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BlockchainOptions> options,
        ILogger<BlockchainOperationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var opts = options.Value;
        _maxAttempts = opts.MaxAttempts;
        _batchSize = opts.BatchSize;
        _pollInterval = TimeSpan.FromSeconds(opts.PollSeconds);
        _autoConfirmSubmitted = opts.AutoConfirmSubmitted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                await ReconcileSubmittedAsync(context, stoppingToken);
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
        var tokenCount = root.TryGetProperty("tokenCount", out var c) && c.TryGetInt64(out var parsed)
            ? parsed
            : 0;
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

    /// <summary>Reconciliation pass: marks submitted operations as confirmed once finalized.</summary>
    private async Task ReconcileSubmittedAsync(AtriaDbContext context, CancellationToken ct)
    {
        // Finality is gated by configuration and OFF by default: an operation stays Submitted until
        // something actually verifies it on chain. The auto-confirm path below makes no chain call at
        // all — it exists only for local runs against a signer stub, and a real implementation must
        // fetch the receipt for operation.TransactionRef and confirm on finality instead.
        if (!_autoConfirmSubmitted)
            return;

        _logger.LogWarning(
            "Blockchain:AutoConfirmSubmitted is ON: submitted operations are being marked confirmed "
            + "WITHOUT any on-chain finality check. This must never be enabled in production.");

        var submitted = await context.Set<BlockchainOperation>()
            .Where(o => o.Status == BlockchainOperationStatus.Submitted)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var operation in submitted)
        {
            operation.MarkConfirmed();

            _logger.LogInformation(
                "Confirmed blockchain operation {OperationId} ({Type}); tx {TransactionRef}.",
                operation.Id, operation.Type, operation.TransactionRef);
        }

        if (submitted.Count > 0)
            await context.SaveChangesAsync(ct);
    }
}
