using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>
/// Terminal: the holder exercised the 14-day right of withdrawal. The shares are gone from them and
/// the refund is owed, so there is nothing left to approve, decline or withdraw again.
/// </summary>
public sealed class WithdrawnState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Withdrawn;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("A withdrawn investment cannot be approved.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("A withdrawn investment cannot be rejected.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("A withdrawn investment cannot be cancelled.");

    public IInvestmentState Expire(Investment investment)
        => throw new InvalidStateTransitionException("A withdrawn investment cannot expire.");

    public IInvestmentState Withdraw(Investment investment)
        => throw new InvalidStateTransitionException("This investment has already been withdrawn.");

    public static WithdrawnState Instance { get; } = new();
    private WithdrawnState() { }
}
