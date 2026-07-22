using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: the application was cancelled by the investor. No further transitions.</summary>
public sealed class CancelledState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Cancelled;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("Cannot approve a cancelled application.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("Cannot reject a cancelled application.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("The application is already cancelled.");

    public static CancelledState Instance { get; } = new();
    private CancelledState() { }
}
