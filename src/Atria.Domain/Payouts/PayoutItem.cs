using Atria.Domain.Common;

namespace Atria.Domain.Payouts;

/// <summary>
/// What one holder is owed out of one distribution, and whether it reached them.
/// </summary>
/// <remarks>
/// The address and the share count are copied off the snapshot rather than referenced, so the line
/// still says what it was computed from after the register has moved on. A payout that has to be
/// re-explained a year later is explained by these numbers, not by today's holdings.
/// </remarks>
public sealed class PayoutItem : Entity
{
    public Guid PayoutRunId { get; private set; }

    /// <summary>The investor behind the address at the cut.</summary>
    public Guid InvestorId { get; private set; }

    /// <summary>The holding address as of the cut.</summary>
    public string WalletAddress { get; private set; } = null!;

    /// <summary>Shares held at the cut — the basis of this line.</summary>
    public decimal TokenCount { get; private set; }

    /// <summary>Amount owed on this line.</summary>
    public decimal Amount { get; private set; }

    public PayoutItemStatus Status { get; private set; }

    /// <summary>Bank payment order number or transaction hash, once the money has moved.</summary>
    public string? SettlementReference { get; private set; }

    public DateTime? PaidAtUtc { get; private set; }

    /// <summary>Why delivery failed. Kept after a later success, as the history of the line.</summary>
    public string? FailureReason { get; private set; }

    private PayoutItem() { }

    internal static PayoutItem Create(
        Guid payoutRunId, Guid investorId, string walletAddress, decimal tokenCount, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new DomainException("Wallet address is required.");
        if (tokenCount <= 0)
            throw new DomainException("A payout line needs a positive share count.");
        if (amount < 0)
            throw new DomainException("A payout line cannot be negative.");

        return new PayoutItem
        {
            Id = Guid.NewGuid(),
            PayoutRunId = payoutRunId,
            InvestorId = investorId,
            WalletAddress = walletAddress,
            TokenCount = tokenCount,
            Amount = amount,
            Status = PayoutItemStatus.Pending
        };
    }

    /// <summary>Records that this holder was paid, with the reference proving it.</summary>
    internal void Settle(string settlementReference, DateTime nowUtc)
    {
        if (Status == PayoutItemStatus.Paid)
            throw new DomainException("This holder has already been paid.");
        if (string.IsNullOrWhiteSpace(settlementReference))
            throw new DomainException("A settlement reference is required.");

        Status = PayoutItemStatus.Paid;
        SettlementReference = settlementReference;
        PaidAtUtc = nowUtc;
    }

    /// <summary>
    /// Records that delivery failed. The line stays on the run and keeps its amount: a payment that
    /// bounced is still owed, and dropping it would quietly reduce what the issue distributed.
    /// </summary>
    internal void Fail(string reason)
    {
        if (Status == PayoutItemStatus.Paid)
            throw new DomainException("A paid line cannot be marked failed.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failure reason is required.");

        Status = PayoutItemStatus.Failed;
        FailureReason = reason;
    }
}
