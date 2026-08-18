using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: an operator voided the application. No further transitions.</summary>
public sealed class AnnulledState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Annulled;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("Cannot approve an annulled application.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("Cannot reject an annulled application.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("Cannot cancel an annulled application.");

    public IInvestmentState Expire(Investment investment)
        => throw new InvalidStateTransitionException("Cannot expire an annulled application.");

    public IInvestmentState Withdraw(Investment investment)
        => throw new InvalidStateTransitionException("Cannot withdraw from an annulled application.");

    public IInvestmentState Annul(Investment investment, string reason)
        => throw new InvalidStateTransitionException("This application is already annulled.");

    public static AnnulledState Instance { get; } = new();
    private AnnulledState() { }
}
