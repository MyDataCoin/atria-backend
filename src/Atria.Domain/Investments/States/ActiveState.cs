using Atria.Domain.Common;

namespace Atria.Domain.Investments.States;

/// <summary>Terminal state: application approved, investment is live. No further transitions.</summary>
public sealed class ActiveState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Active;

    public IInvestmentState Approve(Investment investment)
        => throw new InvalidStateTransitionException("Investment is already active; it cannot be approved again.");

    public IInvestmentState Reject(Investment investment, string reason)
        => throw new InvalidStateTransitionException("Cannot reject an already active investment.");

    public IInvestmentState Cancel(Investment investment)
        => throw new InvalidStateTransitionException("Cannot cancel an already active investment.");

    public IInvestmentState Expire(Investment investment)
        => throw new InvalidStateTransitionException("Cannot expire an already active investment.");

    public IInvestmentState Withdraw(Investment investment)
    {
        investment.RaiseDomainEvent(new Events.InvestmentWithdrawnEvent(
            investment.Id, investment.InvestorId, investment.PropertyId,
            investment.TokenCount, investment.Amount, investment.Currency));

        return WithdrawnState.Instance;
    }

    public static ActiveState Instance { get; } = new();
    private ActiveState() { }
}
