using Atria.Domain.Common;
using Atria.Domain.Investments.Events;
using Atria.Domain.Investments.States;

namespace Atria.Domain.Investments;

/// <summary>
/// An investor's token purchase. Created (PendingPayment) from an approved application,
/// then driven through its lifecycle by the State pattern as payments resolve.
/// </summary>
public sealed class Investment : AggregateRoot
{
    public Guid InvestorId { get; private set; }
    public Guid PropertyId { get; private set; }
    public Guid ApplicationId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public InvestmentStatus Status { get; private set; }

    private readonly List<PaymentTransaction> _payments = new();
    public IReadOnlyCollection<PaymentTransaction> Payments => _payments.AsReadOnly();

    // private ctor: creation only through the factory
    private Investment() { }

    // Used by InvestmentFactory (same assembly) to build a PendingPayment investment.
    internal static Investment CreatePending(
        Guid applicationId, Guid investorId, Guid propertyId, decimal amount, string currency)
        => new()
        {
            Id = Guid.NewGuid(),
            ApplicationId = applicationId,
            InvestorId = investorId,
            PropertyId = propertyId,
            Amount = amount,
            Currency = currency,
            Status = InvestmentStatus.PendingPayment
        };

    /// <summary>PendingPayment -> Active: adds a Completed payment and raises completion + activation events.</summary>
    public void ConfirmPayment(PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
        => Status = InvestmentStateFactory.Create(Status)
            .ConfirmPayment(this, provider, externalPaymentId, amount, currency).Status;

    /// <summary>PendingPayment -> Failed: adds a Failed payment and raises a payment-failed event.</summary>
    public void FailPayment(PaymentProviderType provider, string reason)
        => Status = InvestmentStateFactory.Create(Status).FailPayment(this, provider, reason).Status;

    // Lets the state objects (same assembly) append child transactions to this aggregate.
    internal void AddPayment(PaymentTransaction transaction) => _payments.Add(transaction);

    // Lets the state objects raise events through the protected base method.
    internal void RaiseDomainEvent(IDomainEvent e) => base.RaiseEvent(e);
}
