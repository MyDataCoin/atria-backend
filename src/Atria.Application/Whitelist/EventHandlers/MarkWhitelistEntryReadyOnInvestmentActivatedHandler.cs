using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;
using Atria.Domain.Whitelist;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Whitelist.EventHandlers;

/// <summary>
/// Makes a queued request mintable once an operator approves the application. Also fills in the wallet
/// if the investor had none when they applied — by approval time the compliance profile is the address
/// the on-chain allowlist was written with, and the mint has to go to that same address.
/// </summary>
public sealed class MarkWhitelistEntryReadyOnInvestmentActivatedHandler
    : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly IWhitelistEntryRepository _entries;
    private readonly IComplianceRepository _profiles;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<MarkWhitelistEntryReadyOnInvestmentActivatedHandler> _logger;

    public MarkWhitelistEntryReadyOnInvestmentActivatedHandler(
        IWhitelistEntryRepository entries,
        IComplianceRepository profiles,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<MarkWhitelistEntryReadyOnInvestmentActivatedHandler> logger)
    {
        _entries = entries;
        _profiles = profiles;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        var entry = await _entries.GetByInvestmentAsync(domainEvent.InvestmentId, ct);
        if (entry is null)
        {
            // The queue is fed by the creation event, so a missing entry means that one never landed.
            // Queue it here rather than lose the request: an approved purchase with nothing to mint
            // against would otherwise sit invisible.
            var profile = await _profiles.GetByInvestorAsync(domainEvent.InvestorId, ct);
            entry = WhitelistEntry.Queue(
                domainEvent.InvestmentId, domainEvent.InvestorId, domainEvent.PropertyId,
                domainEvent.TokenCount, profile?.WalletAddress, domainEvent.OccurredOnUtc);
            await _entries.AddAsync(entry, ct);

            _logger.LogWarning(
                "No whitelist request for activated investment {InvestmentId}; queued it on activation.",
                domainEvent.InvestmentId);
        }
        else if (string.IsNullOrWhiteSpace(entry.WalletAddress))
        {
            var profile = await _profiles.GetByInvestorAsync(domainEvent.InvestorId, ct);
            if (!string.IsNullOrWhiteSpace(profile?.WalletAddress))
                entry.SetWallet(profile.WalletAddress);
        }

        if (entry.Status == WhitelistStatus.Pending)
        {
            entry.MarkReady(domainEvent.OccurredOnUtc);
            _entries.Update(entry);
        }

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
