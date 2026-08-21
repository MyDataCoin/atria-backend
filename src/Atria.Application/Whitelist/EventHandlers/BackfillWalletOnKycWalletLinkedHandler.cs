using Atria.Application.Abstractions;
using Atria.Domain.Kyc.Events;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Whitelist.EventHandlers;

/// <summary>
/// Carries a late-linked wallet to the places that need it: the investor's compliance profile, and
/// every whitelist request of theirs still waiting for an address.
/// </summary>
/// <remarks>
/// <para>
/// The wallet used to reach those places only through <c>KycApprovedEvent</c>, which fires at
/// verification — but the product collects the address AFTER verification, so in the ordinary flow the
/// address simply did not exist yet at that moment. The compliance profile has no way to be given one
/// later, and the whitelist copies from the compliance profile, so an investor who linked their wallet
/// a day after passing KYC stayed "no wallet" in the operator's queue forever, unbatchable, with
/// nothing in the UI hinting at why.
/// </para>
/// <para>
/// Requests already handed to the exchange are deliberately left alone: a batch that went out naming
/// one address must keep naming it, or what comes back will not reconcile. Exactly-once via
/// <see cref="IProcessedEventStore"/>.
/// </para>
/// </remarks>
public sealed class BackfillWalletOnKycWalletLinkedHandler : IDomainEventHandler<KycWalletLinkedEvent>
{
    private readonly IComplianceRepository _profiles;
    private readonly IWhitelistEntryRepository _entries;
    private readonly IProcessedEventStore _processed;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<BackfillWalletOnKycWalletLinkedHandler> _logger;

    public BackfillWalletOnKycWalletLinkedHandler(
        IComplianceRepository profiles,
        IWhitelistEntryRepository entries,
        IProcessedEventStore processed,
        IUnitOfWork uow,
        ILogger<BackfillWalletOnKycWalletLinkedHandler> logger)
    {
        _profiles = profiles;
        _entries = entries;
        _processed = processed;
        _uow = uow;
        _logger = logger;
    }

    public async Task HandleAsync(KycWalletLinkedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processed.IsProcessedAsync(key, ct))
            return;

        // The investor is the user behind the KYC profile — the same identity the compliance profile
        // and the whitelist are keyed by.
        var investorId = domainEvent.UserId;

        // The profile may legitimately not exist yet: linking a wallet before approval leaves the
        // KycApprovedEvent handler to create it, and that handler reads the address off this same KYC
        // aggregate, so nothing is lost by doing nothing here.
        var profile = await _profiles.GetByInvestorAsync(investorId, ct);
        if (profile is not null && profile.SetWalletIfMissing(domainEvent.WalletAddress))
            _profiles.Update(profile);

        var waiting = await _entries.ListAwaitingWalletAsync(investorId, ct);
        foreach (var entry in waiting)
        {
            entry.SetWallet(domainEvent.WalletAddress);
            _entries.Update(entry);
        }

        if (waiting.Count > 0)
            _logger.LogInformation(
                "Wallet linked for investor {InvestorId}; filled it into {Count} whitelist request(s).",
                investorId, waiting.Count);

        await _processed.MarkProcessedAsync(key, ct);
        await _uow.SaveChangesAsync(ct);
    }
}
