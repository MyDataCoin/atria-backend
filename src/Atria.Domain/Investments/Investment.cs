using Atria.Domain.Common;
using Atria.Domain.Investments.Events;
using Atria.Domain.Investments.States;

namespace Atria.Domain.Investments;

/// <summary>
/// An investor's token purchase. Created (PendingPayment) directly by the investor,
/// then driven through its lifecycle by the State pattern as payments resolve.
/// </summary>
public sealed class Investment : AggregateRoot
{
    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }

    /// <summary>How many tokens the investor bought.</summary>
    public long TokenCount { get; private set; }

    /// <summary>The amount the tokens were bought for, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public InvestmentStatus Status { get; private set; }

    /// <summary>
    /// Referral token of the deal this purchase was made under, if the investor arrived via a
    /// realtor's link. Carried so the deal can be settled when the investment activates; null otherwise.
    /// </summary>
    public string? ReferralToken { get; private set; }

    // private ctor: creation only through the factory
    private Investment() { }

    // Used by InvestmentFactory (same assembly) to build a PendingPayment investment.
    internal static Investment CreatePending(
        Guid investorId, Guid propertyId, long tokenCount, decimal amount, string currency,
        string? referralToken)
        => new()
        {
            Id = Guid.NewGuid(),
            InvestorId = investorId,
            PropertyId = propertyId,
            TokenCount = tokenCount,
            Amount = amount,
            Currency = currency,
            Status = InvestmentStatus.PendingPayment,
            ReferralToken = referralToken
        };

    /// <summary>PendingPayment -> Active: raises payment-completion + activation events.</summary>
    public void ConfirmPayment(PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
        => Status = InvestmentStateFactory.Create(Status)
            .ConfirmPayment(this, provider, externalPaymentId, amount, currency).Status;

    /// <summary>PendingPayment -> Failed: raises a payment-failed event.</summary>
    public void FailPayment(PaymentProviderType provider, string reason)
        => Status = InvestmentStateFactory.Create(Status).FailPayment(this, provider, reason).Status;

    // Lets the state objects raise events through the protected base method.
    internal void RaiseDomainEvent(IDomainEvent e) => base.RaiseEvent(e);
}
