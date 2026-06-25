using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: investment was cancelled. No payment transitions allowed.</summary>
public sealed class CancelledState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Cancelled;

    public IInvestmentState ConfirmPayment(
        Investment investment, PaymentProviderType provider, string externalPaymentId, decimal amount, string currency)
        => throw new InvalidStateTransitionException("Cannot confirm payment for a cancelled investment.");

    public IInvestmentState FailPayment(Investment investment, PaymentProviderType provider, string reason)
        => throw new InvalidStateTransitionException("Cannot fail payment for a cancelled investment.");

    public static CancelledState Instance { get; } = new();
    private CancelledState() { }
}
