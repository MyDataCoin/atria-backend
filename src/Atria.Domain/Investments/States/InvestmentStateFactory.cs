using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>
/// Stateless factory that maps the persisted <see cref="InvestmentStatus"/> enum to its
/// singleton state object. Keeps EF rehydration to a single column (no _state field).
/// </summary>
public static class InvestmentStateFactory
{
    public static IInvestmentState Create(InvestmentStatus status) => status switch
    {
        InvestmentStatus.PendingPayment => PendingPaymentState.Instance,
        InvestmentStatus.Active => ActiveState.Instance,
        InvestmentStatus.Failed => FailedState.Instance,
        InvestmentStatus.Cancelled => CancelledState.Instance,
        _ => throw new InvalidStateTransitionException($"Unknown investment status: {status}.")
    };
}
