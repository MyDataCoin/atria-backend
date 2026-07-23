using Atria.Application.Abstractions;
using Atria.Domain.Holders;
using Atria.Domain.Investments.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Holders.EventHandlers;

/// <summary>
/// Projects the holder registry from our own records: when an application activates, the investor's
/// wallet gains its tokens in the (property, wallet) position. This is the temporary source of truth
/// until chain reading is wired — at which point positions are reconciled against on-chain balances
/// (<see cref="HolderPosition.Sync"/>) instead of accumulated here. Exactly-once via
/// <see cref="IProcessedEventStore"/> so a redelivered activation never double-counts the shares.
/// </summary>
public sealed class ProjectHolderPositionOnInvestmentActivatedHandler
    : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly IComplianceRepository _profiles;
    private readonly IHolderPositionRepository _positions;
    private readonly IProcessedEventStore _processed;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<ProjectHolderPositionOnInvestmentActivatedHandler> _logger;

    public ProjectHolderPositionOnInvestmentActivatedHandler(
        IComplianceRepository profiles,
        IHolderPositionRepository positions,
        IProcessedEventStore processed,
        IDateTimeProvider clock,
        IUnitOfWork uow,
        ILogger<ProjectHolderPositionOnInvestmentActivatedHandler> logger)
    {
        _profiles = profiles;
        _positions = positions;
        _processed = processed;
        _clock = clock;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var investorId = domainEvent.InvestorId;

        // The wallet (and its allowlist standing) come from the compliance profile — the same source
        // the on-chain allowlist/allocation uses, so the registry and the chain agree on the address.
        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        var wallet = profile?.WalletAddress;
        if (string.IsNullOrWhiteSpace(wallet))
        {
            // No wallet to key a position on. The gap surfaces in reconciliation once chain reading
            // exists; the shares are not lost, they just have no address to sit on yet.
            _logger.LogWarning(
                "No wallet for investor {InvestorId} on activation {InvestmentId}; holder position not projected.",
                investorId, domainEvent.InvestmentId);
            await _processed.MarkProcessedAsync(key, ct);
            return;
        }

        var now = _clock.UtcNow;
        var position = await _positions.GetByAddressAsync(domainEvent.PropertyId, wallet, ct);
        if (position is null)
        {
            position = HolderPosition.Create(
                domainEvent.PropertyId, wallet, domainEvent.TokenCount, investorId,
                isAllowlisted: profile!.IsAllowlisted, HolderSource.OurRecords, now);
            await _positions.AddAsync(position, ct);
        }
        else
        {
            position.Increase(domainEvent.TokenCount, now);
            position.LinkInvestor(investorId);
            position.SetAllowlisted(profile!.IsAllowlisted);
            _positions.Update(position);
        }

        await _uow.SaveChangesAsync(ct);
        await _processed.MarkProcessedAsync(key, ct);
    }
}
