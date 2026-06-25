using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: payment failed. No further transitions.</summary>
public sealed class FailedState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Failed;

    public IInvestmentState ConfirmPayment(
        Investment investment, PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
        => throw new InvalidStateTransitionException("Cannot confirm payment for a failed investment.");

    public IInvestmentState FailPayment(Investment investment, PaymentProviderType provider, string reason)
        => throw new InvalidStateTransitionException("Investment payment has already failed.");

    public static FailedState Instance { get; } = new();
    private FailedState() { }
}
