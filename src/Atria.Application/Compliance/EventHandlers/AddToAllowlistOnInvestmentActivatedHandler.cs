using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Domain.Investments.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Compliance.EventHandlers;

/// <summary>
/// When an application is approved (the investment activates), verifies the investor's presentation
/// against the project policy and, if it holds, adds their wallet to the permissioned allowlist and
/// enqueues a token allocation (mint) on chain. AllowlistAdd always precedes TokenAllocation because
/// the permissioned token only accepts a mint to a whitelisted address. Exactly-once via
/// <see cref="IProcessedEventStore"/> so the allowlist + token effects happen at most once.
/// </summary>
public sealed class AddToAllowlistOnInvestmentActivatedHandler : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly IComplianceRepository _profiles;
    private readonly ITesseraComplianceService _tessera;
    private readonly IBlockchainOperationQueue _queue;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<AddToAllowlistOnInvestmentActivatedHandler> _logger;

    public AddToAllowlistOnInvestmentActivatedHandler(
        IComplianceRepository profiles,
        ITesseraComplianceService tessera,
        IBlockchainOperationQueue queue,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<AddToAllowlistOnInvestmentActivatedHandler> logger)
    {
        _profiles = profiles;
        _tessera = tessera;
        _queue = queue;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var investorId = domainEvent.InvestorId;

        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is null)
        {
            // No compliance profile yet (DID not issued) — nothing to allowlist.
            _logger.LogWarning(
                "No compliance profile for investor {InvestorId}; skipping allowlist/token allocation.",
                investorId);
            await _processed.MarkProcessedAsync(key, ct);
            return;
        }

        // NOTE: the configured project policy id is resolved inside the service
        // implementation (TesseraOptions.PolicyId); the Application layer does not hold it.
        var verified = await _tessera.VerifyPresentationAsync(investorId, string.Empty, ct);
        if (!verified)
        {
            _logger.LogWarning(
                "Presentation verification failed for investor {InvestorId}; not allowlisting.",
                investorId);
            await _processed.MarkProcessedAsync(key, ct);
            return;
        }

        var wallet = profile.WalletAddress;
        if (string.IsNullOrWhiteSpace(wallet))
        {
            // Without a wallet we cannot allowlist or allocate tokens on chain.
            _logger.LogWarning(
                "No wallet address for investor {InvestorId}; skipping on-chain allowlist/token allocation.",
                investorId);
            await _processed.MarkProcessedAsync(key, ct);
            return;
        }

        await _tessera.AddToAllowlistAsync(wallet, ct);
        profile.MarkAllowlisted();
        _profiles.Update(profile);

        // Durable, idempotent on-chain token allocation (mint of TokenCount to the investor's wallet);
        // the idempotency key ties it to this activation. Built via System.Text.Json so values (wallet)
        // containing a quote/backslash cannot produce malformed JSON.
        var payload = JsonSerializer.Serialize(new
        {
            investmentId = domainEvent.InvestmentId,
            investorId,
            propertyId = domainEvent.PropertyId,
            wallet,
            tokenCount = domainEvent.TokenCount
        });
        var idempotencyKey = $"TokenAllocation:{domainEvent.EventId}";
        await _queue.EnqueueAsync(BlockchainOperationType.TokenAllocation, payload, idempotencyKey, ct);

        await _uow.SaveChangesAsync(ct);
        await _processed.MarkProcessedAsync(key, ct);
    }
}
