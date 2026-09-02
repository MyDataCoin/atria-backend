using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Domain.Investments;
using Atria.Domain.Refunds;
using Atria.Domain.Whitelist;

namespace Atria.Application.Investments.Services;

/// <summary>
/// Undoes a single offering application: the shares go back to the pool, and if any had actually
/// been placed, the money owed is recorded, the register stops counting them and the on-chain
/// balance is queued for burning.
/// </summary>
/// <remarks>
/// <para>
/// Extracted because two callers now need exactly this and must not drift apart: an operator voiding
/// one application, and a placement that closed short of its target being unwound in full. The burn
/// is the part that must not be reimplemented — deciding which mints actually reached an address is
/// subtle enough that a second copy would eventually get it wrong and leave shares alive on chain
/// after their money was returned.
/// </para>
/// <para>
/// Nothing here saves. The caller owns the unit of work, which is what lets a whole placement be
/// unwound as one transaction rather than one per applicant.
/// </para>
/// </remarks>
public sealed class InvestmentUnwinder
{
    private readonly IHolderPositionRepository _positions;
    private readonly IRefundObligationRepository _refunds;
    private readonly IComplianceRepository _profiles;
    private readonly IWhitelistEntryRepository _whitelist;
    private readonly IBlockchainOperationQueue _chain;
    private readonly IDateTimeProvider _clock;

    public InvestmentUnwinder(
        IHolderPositionRepository positions,
        IRefundObligationRepository refunds,
        IComplianceRepository profiles,
        IWhitelistEntryRepository whitelist,
        IBlockchainOperationQueue chain,
        IDateTimeProvider clock)
    {
        _positions = positions;
        _refunds = refunds;
        _profiles = profiles;
        _whitelist = whitelist;
        _chain = chain;
        _clock = clock;
    }

    /// <summary>
    /// Records everything owed and queued for one application that has just left a live status.
    /// Call AFTER the transition and AFTER the shares are released back to the property.
    /// </summary>
    /// <param name="property">The issue the application belongs to.</param>
    /// <param name="investment">The application, already transitioned out of its live status.</param>
    /// <param name="wasActive">
    /// Whether shares had actually been placed. Read BEFORE the transition — afterwards the status no
    /// longer says. A reserved application never held shares, so it has nothing to refund or burn.
    /// </param>
    /// <param name="reason">Why the application is being unwound, for the refund and the burn.</param>
    /// <param name="refundReason">The ground the refund is recorded under.</param>
    /// <param name="burnTag">
    /// Short slug identifying the cause in the burn's idempotency key (e.g. <c>annulment</c>).
    /// </param>
    /// <param name="burnReason">
    /// The cause as written into the burn payload itself (e.g. <c>AnnulledByOperator</c>). Kept apart
    /// from <paramref name="burnTag"/>: the tag keys deduplication, this is what the chain worker
    /// records as the reason, and the two have never been the same string.
    /// </param>
    /// <param name="recordRefund">Whether money is actually owed back. False when it was never paid.</param>
    public async Task UnwindAsync(
        Property property,
        Investment investment,
        bool wasActive,
        string reason,
        RefundReason refundReason,
        string burnTag,
        string burnReason,
        bool recordRefund,
        CancellationToken ct)
    {
        if (!wasActive)
            return;

        // The address the shares actually went to. The whitelist request snapshots it at mint time,
        // so a profile whose wallet changed afterwards cannot send the burn to the wrong holder.
        var entry = await _whitelist.GetByInvestmentAsync(investment.Id, ct);
        var profile = await _profiles.GetByInvestorAsync(investment.InvestorId, ct);
        var wallet = entry?.WalletAddress ?? investment.WalletAddress ?? profile?.WalletAddress;

        if (recordRefund)
            await _refunds.AddAsync(RefundObligation.Raise(
                property.Id, investment.InvestorId, wallet ?? "—", investment.TokenCount,
                investment.Amount, investment.Currency, refundReason, reason), ct);

        if (string.IsNullOrWhiteSpace(wallet))
            return;

        await StopCountingInRegistryAsync(property.Id, wallet, investment.TokenCount, ct);

        // Only what actually reached the address needs burning. Either witness is enough: the
        // platform's own allocation writes OnChainStatus, a batch handed to the exchange only
        // marks the request Minted — reading just one leaves the other kind of mint un-burned.
        if ((investment.OnChainStatus == OnChainStatus.None && entry?.Status != WhitelistStatus.Minted)
            || string.IsNullOrWhiteSpace(property.TokenContractAddress))
            return;

        var payload = JsonSerializer.Serialize(new
        {
            propertyId = property.Id,
            chain = property.TokenChain,
            tokenContractAddress = property.TokenContractAddress,
            holder = wallet,
            amount = investment.TokenCount,
            reason = burnReason
        });

        await _chain.EnqueueAsync(
            BlockchainOperationType.TokenBurn, payload,
            $"token-burn:{burnTag}:{investment.Id}", ct);
    }

    /// <summary>Takes the shares out of the holder's position so the register stops counting them.</summary>
    private async Task StopCountingInRegistryAsync(
        Guid propertyId, string wallet, long tokenCount, CancellationToken ct)
    {
        var position = await _positions.GetByAddressAsync(propertyId, wallet, ct);
        if (position is null)
            return;

        position.Sync(Math.Max(position.TokenCount - tokenCount, 0), position.Source, _clock.UtcNow);
        _positions.Update(position);
    }
}
