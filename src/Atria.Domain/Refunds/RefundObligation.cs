using Atria.Domain.Common;

namespace Atria.Domain.Refunds;

/// <summary>Why money is owed back to a holder.</summary>
public enum RefundReason
{
    /// <summary>The issue was declared invalid; shares are withdrawn and funds returned (§73).</summary>
    IssueInvalidated = 0,

    /// <summary>The holder exercised the 14-day right of withdrawal (§44).</summary>
    WithdrawalWithin14Days = 1,

    /// <summary>Part of the issue was annulled and the affected holders are made whole (ch. 11).</summary>
    IssueAnnulled = 2
}

/// <summary>Where a refund stands.</summary>
public enum RefundStatus
{
    /// <summary>Recorded and owed. The shares are being withdrawn; the money has not moved.</summary>
    Pending = 0,

    /// <summary>Paid back to the holder.</summary>
    Settled = 1
}

/// <summary>
/// Money the issuer owes a holder back. Recorded the moment the obligation arises — when an issue is
/// declared invalid, or a holder withdraws within 14 days — so what is owed exists on the books
/// before anyone gets round to paying it.
///
/// Recording is deliberately separate from paying: how funds actually reach an investor is a decision
/// outside the platform (bank account or wallet), and an obligation nobody has written down is one
/// nobody settles.
/// </summary>
public sealed class RefundObligation : AggregateRoot
{
    public Guid PropertyId { get; private set; }
    public Guid InvestorId { get; private set; }

    /// <summary>The address the shares are being withdrawn from.</summary>
    public string WalletAddress { get; private set; } = null!;

    /// <summary>Shares withdrawn from circulation against this refund.</summary>
    public long TokenCount { get; private set; }

    /// <summary>Amount owed, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    public RefundReason Reason { get; private set; }
    public RefundStatus Status { get; private set; }

    /// <summary>Free-text note carried from the decision that created the obligation.</summary>
    public string? Note { get; private set; }

    public DateTime? SettledAtUtc { get; private set; }

    /// <summary>How the money was returned — a bank reference or a transaction hash.</summary>
    public string? SettlementReference { get; private set; }

    private RefundObligation() { }

    public static RefundObligation Raise(
        Guid propertyId, Guid investorId, string walletAddress, long tokenCount,
        decimal amount, string currency, RefundReason reason, string? note)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new DomainException("Wallet address is required.");
        if (tokenCount <= 0)
            throw new DomainException("Refunded token count must be positive.");
        if (amount <= 0)
            throw new DomainException("Refund amount must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        return new RefundObligation
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            InvestorId = investorId,
            WalletAddress = walletAddress,
            TokenCount = tokenCount,
            Amount = amount,
            Currency = currency,
            Reason = reason,
            Status = RefundStatus.Pending,
            Note = note
        };
    }

    /// <summary>Records that the money was returned, with the reference proving it.</summary>
    public void Settle(string settlementReference, DateTime nowUtc)
    {
        if (Status == RefundStatus.Settled)
            throw new DomainException("This refund has already been settled.");
        if (string.IsNullOrWhiteSpace(settlementReference))
            throw new DomainException("A settlement reference is required.");

        Status = RefundStatus.Settled;
        SettlementReference = settlementReference;
        SettledAtUtc = nowUtc;
    }
}
