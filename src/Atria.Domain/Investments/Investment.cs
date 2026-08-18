using Atria.Domain.Common;
using Atria.Domain.Investments.States;

namespace Atria.Domain.Investments;

/// <summary>
/// An investor's offering application. Created (Reserved) directly by the investor — which holds
/// tokens from the property's pool — then driven through its lifecycle by the State pattern: an
/// operator approves it (Active), or it is rejected/cancelled (returning the reserved tokens). There
/// is no payment on the platform.
/// </summary>
public sealed class Investment : AggregateRoot
{
    /// <summary>Calendar days a holder has to walk away from a primary purchase (draft Decree, §44).</summary>
    public const int WithdrawalPeriodDays = 14;

    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }

    /// <summary>How many tokens the investor applied for (reserved, then allocated on approval).</summary>
    public decimal TokenCount { get; private set; }

    /// <summary>
    /// The amount snapshot for the application, in <see cref="Currency"/>. A point-in-time record of
    /// the price at application; the position's worth is <see cref="TokenCount"/> ×
    /// <see cref="PricePerToken"/>. Money is not settled on the platform.
    /// </summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    /// <summary>Unit token price snapshot at the time the application was made.</summary>
    public decimal PricePerToken { get; private set; }

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public InvestmentStatus Status { get; private set; }

    /// <summary>
    /// When the reservation lapses if not yet approved. Reserved tokens can be returned to the pool
    /// after this instant. Set at creation.
    /// </summary>
    public DateTime ReservedUntilUtc { get; private set; }

    /// <summary>
    /// When the application was activated by an operator. Null while it has not been. Drives which
    /// reporting period a placement falls into.
    /// </summary>
    public DateTime? ActivatedAtUtc { get; private set; }

    /// <summary>
    /// Until when the holder may walk away without giving a reason (draft Decree, §44): 14 calendar
    /// days from activation. Null until the application is activated.
    /// </summary>
    /// <remarks>
    /// Calendar days, not working days — §44 says so, and it is the one deadline that runs in the
    /// investor's favour rather than the issuer's.
    /// </remarks>
    public DateTime? WithdrawalDeadlineUtc { get; private set; }

    /// <summary>True while the holder can still withdraw.</summary>
    public bool CanWithdrawAt(DateTime nowUtc)
        => Status == InvestmentStatus.Active
           && WithdrawalDeadlineUtc is not null
           && nowUtc <= WithdrawalDeadlineUtc;

    /// <summary>
    /// Why an operator refused the application — declined it, or later annulled it. Set on that
    /// decision and kept afterwards so the investor can be told the reason and it stays auditable;
    /// null in every other state. Which of the two it was is told by <see cref="Status"/>.
    /// </summary>
    public string? RejectionReason { get; private set; }

    // --- On-chain settlement (filled once chain wiring is enabled; null/None until then) ---

    /// <summary>Wallet the tokens are allocated to (the investor's whitelisted address).</summary>
    public string? WalletAddress { get; private set; }

    /// <summary>Address of the property's permissioned token contract the tokens were minted on.</summary>
    public string? TokenContractAddress { get; private set; }

    /// <summary>Hash of the mint transaction, once submitted on chain.</summary>
    public string? TransactionHash { get; private set; }

    /// <summary>On-chain confirmation status of the token allocation (mint).</summary>
    public OnChainStatus OnChainStatus { get; private set; }

    /// <summary>
    /// Referral token of the deal this application was made under, if the investor arrived via a
    /// realtor's link. Carried so the deal can be settled when the investment activates; null otherwise.
    /// </summary>
    public string? ReferralToken { get; private set; }

    // private ctor: creation only through the factory
    private Investment() { }

    // Used by InvestmentFactory (same assembly) to build a Reserved application.
    internal static Investment CreateReserved(
        Guid investorId, Guid propertyId, decimal tokenCount, decimal amount, string currency,
        decimal pricePerToken, DateTime reservedUntilUtc, string? referralToken)
        => new()
        {
            Id = Guid.NewGuid(),
            InvestorId = investorId,
            PropertyId = propertyId,
            TokenCount = tokenCount,
            Amount = amount,
            Currency = currency,
            PricePerToken = pricePerToken,
            ReservedUntilUtc = reservedUntilUtc,
            Status = InvestmentStatus.Reserved,
            OnChainStatus = OnChainStatus.None,
            ReferralToken = referralToken
        };

    /// <summary>Reserved -> Active: an operator approves the application. Raises the activation event.</summary>
    /// <remarks>
    /// The activation instant is recorded because the regulator's placement figures are per period:
    /// an application made in one month and approved in the next belongs to the month it activated in.
    /// </remarks>
    public void Approve(DateTime activatedAtUtc)
    {
        Status = InvestmentStateFactory.Create(Status).Approve(this).Status;
        ActivatedAtUtc = activatedAtUtc;
        WithdrawalDeadlineUtc = activatedAtUtc.AddDays(WithdrawalPeriodDays);
    }

    /// <summary>
    /// Active -> Withdrawn: the holder exercises the 14-day right of withdrawal. Refused once the
    /// window has closed — after that the deal is final and the shares are theirs.
    /// </summary>
    public void Withdraw(DateTime nowUtc)
    {
        if (!CanWithdrawAt(nowUtc))
            throw new DomainException(
                "The 14-day withdrawal window for this investment is not open.");

        Status = InvestmentStateFactory.Create(Status).Withdraw(this).Status;
    }

    /// <summary>Reserved -> Rejected: an operator declines the application. Raises the rejected event.</summary>
    /// <remarks>
    /// The reason is recorded only once the transition is accepted, so a rejected-state guard cannot
    /// leave a reason behind on an application that stayed as it was.
    /// </remarks>
    public void Reject(string reason)
    {
        Status = InvestmentStateFactory.Create(Status).Reject(this, reason).Status;
        RejectionReason = reason;
    }

    /// <summary>Reserved -> Cancelled: the investor withdraws the application. Raises the cancelled event.</summary>
    public void Cancel()
        => Status = InvestmentStateFactory.Create(Status).Cancel(this).Status;

    /// <summary>Reserved -> Expired: the reservation window lapsed without approval. Raises the expired event.</summary>
    public void Expire()
        => Status = InvestmentStateFactory.Create(Status).Expire(this).Status;

    /// <summary>
    /// Reserved|Active -> Annulled: an operator voids an application that should not stand — a mistake,
    /// a duplicate, or leftover test data. The caller returns the shares to the pool.
    /// </summary>
    /// <remarks>
    /// The one transition that leaves <see cref="InvestmentStatus.Active"/> outside the statutory
    /// withdrawal window, and it exists because the alternative is worse: without it such a row can
    /// only be deleted straight from the database, which does not give the shares back and leaves the
    /// issue quietly short of them.
    /// </remarks>
    public void Annul(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An annulment reason is required.");

        Status = InvestmentStateFactory.Create(Status).Annul(this, reason).Status;
        RejectionReason = reason;
    }

    /// <summary>
    /// Records the wallet the tokens are (to be) allocated to and the contract they are minted on.
    /// Set when the on-chain allocation is prepared.
    /// </summary>
    public void SetTokenTarget(string walletAddress, string tokenContractAddress)
    {
        if (string.IsNullOrWhiteSpace(walletAddress))
            throw new DomainException("Wallet address is required.");
        if (string.IsNullOrWhiteSpace(tokenContractAddress))
            throw new DomainException("Token contract address is required.");

        WalletAddress = walletAddress;
        TokenContractAddress = tokenContractAddress;
        OnChainStatus = OnChainStatus.Pending;
    }

    /// <summary>Records the mint transaction hash and its confirmation status from the chain.</summary>
    public void SetOnChainResult(string transactionHash, OnChainStatus status)
    {
        if (string.IsNullOrWhiteSpace(transactionHash))
            throw new DomainException("Transaction hash is required.");

        TransactionHash = transactionHash;
        OnChainStatus = status;
    }

    // Lets the state objects raise events through the protected base method.
    internal void RaiseDomainEvent(IDomainEvent e) => base.RaiseEvent(e);
}
