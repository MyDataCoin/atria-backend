using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: the application was rejected by an operator. No further transitions.</summary>
public sealed class RejectedState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Rejected;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("Cannot approve a rejected application.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("The application is already rejected.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("Cannot cancel a rejected application.");

    public static RejectedState Instance { get; } = new();
    private RejectedState() { }
}
