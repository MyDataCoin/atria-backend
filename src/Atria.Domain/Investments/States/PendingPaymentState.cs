using Atria.Domain.Investments.Events;

namespace Atria.Domain.Investments.States;

/// <summary>Initial state: awaiting payment. Can move to Active (confirm) or Failed (fail).</summary>
public sealed class PendingPaymentState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.PendingPayment;

    public IInvestmentState ConfirmPayment(
        Investment investment, PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
    {
        // Payment completion + activation are distinct facts other modules listen for.
        investment.RaiseDomainEvent(new PaymentCompletedEvent(
            investment.Id, investment.InvestorId, amount, externalPaymentId));
        investment.RaiseDomainEvent(new InvestmentActivatedEvent(
            investment.Id, investment.InvestorId, investment.PropertyId, investment.TokenCount, investment.Amount));

        return ActiveState.Instance;
    }

    public IInvestmentState FailPayment(Investment investment, PaymentProviderType provider, string reason)
    {
        investment.RaiseDomainEvent(new PaymentFailedEvent(investment.Id, investment.InvestorId, reason));
        return FailedState.Instance;
    }

    public static PendingPaymentState Instance { get; } = new();
    private PendingPaymentState() { }
}
