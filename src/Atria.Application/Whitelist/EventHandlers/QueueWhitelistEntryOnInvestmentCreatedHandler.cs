using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;
using Atria.Domain.Whitelist;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Whitelist.EventHandlers;

/// <summary>
/// Puts a purchase into the whitelist queue the moment the investor submits it. This is what makes the
/// queue the single place an operator works from: every request appears there, including the ones that
/// will later be declined, so nothing has to be looked for anywhere else.
/// </summary>
/// <remarks>
/// The wallet is copied from the compliance profile, which exists by then in the normal case — approved
/// KYC is a precondition for buying and issuing the DID is what creates the profile. When it does not,
/// the request is still queued, just without an address: an unminted request that is visible is a
/// problem someone can fix, an invisible one is not. Exactly-once via <see cref="IProcessedEventStore"/>.
/// </remarks>
public sealed class QueueWhitelistEntryOnInvestmentCreatedHandler
    : IDomainEventHandler<InvestmentCreatedEvent>
{
    private readonly IWhitelistEntryRepository _entries;
    private readonly IComplianceRepository _profiles;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<QueueWhitelistEntryOnInvestmentCreatedHandler> _logger;

    public QueueWhitelistEntryOnInvestmentCreatedHandler(
        IWhitelistEntryRepository entries,
        IComplianceRepository profiles,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<QueueWhitelistEntryOnInvestmentCreatedHandler> logger)
    {
        _entries = entries;
        _profiles = profiles;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(InvestmentCreatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        // A request already queued for this purchase (a redelivery that landed before the mark was
        // durable) is left alone rather than duplicated — the unique index would refuse it anyway.
        var existing = await _entries.GetByInvestmentAsync(domainEvent.InvestmentId, ct);
        if (existing is null)
        {
            var profile = await _profiles.GetByInvestorAsync(domainEvent.InvestorId, ct);
            var wallet = profile?.WalletAddress;

            if (string.IsNullOrWhiteSpace(wallet))
                _logger.LogWarning(
                    "No wallet for investor {InvestorId}; queueing request for investment {InvestmentId} without one.",
                    domainEvent.InvestorId, domainEvent.InvestmentId);

            var entry = WhitelistEntry.Queue(
                domainEvent.InvestmentId, domainEvent.InvestorId, domainEvent.PropertyId,
                domainEvent.TokenCount, wallet, domainEvent.OccurredOnUtc);

            await _entries.AddAsync(entry, ct);
        }

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
