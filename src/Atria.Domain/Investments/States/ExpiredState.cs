using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: the reservation lapsed without approval and its tokens were returned. No further transitions.</summary>
public sealed class ExpiredState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Expired;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("Cannot approve an expired application.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("Cannot reject an expired application.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("Cannot cancel an expired application.");

    public IInvestmentState Expire(Investment investment)
        => throw new InvalidStateTransitionException("The application is already expired.");

    public static ExpiredState Instance { get; } = new();
    private ExpiredState() { }
}
