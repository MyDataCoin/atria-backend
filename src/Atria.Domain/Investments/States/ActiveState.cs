using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: payment confirmed, investment is live. No further transitions.</summary>
public sealed class ActiveState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Active;

    public IInvestmentState ConfirmPayment(
        Investment investment, PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
        => throw new InvalidStateTransitionException("Investment is already active; payment cannot be confirmed again.");

    public IInvestmentState FailPayment(Investment investment, PaymentProviderType provider, string reason)
        => throw new InvalidStateTransitionException("Cannot fail the payment of an already active investment.");

    public static ActiveState Instance { get; } = new();
    private ActiveState() { }
}
