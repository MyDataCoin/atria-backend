using Atria.Domain.Investments.Events;

namespace Atria.Domain.Investments.States;

/// <summary>Initial state: application submitted, tokens reserved. Can be approved, rejected or cancelled.</summary>
public sealed class ReservedState : IInvestmentState
{
    public InvestmentStatus Status => InvestmentStatus.Reserved;

    public IInvestmentState Approve(Investment investment)
    {
        investment.RaiseDomainEvent(new InvestmentActivatedEvent(
            investment.Id, investment.InvestorId, investment.PropertyId, investment.TokenCount, investment.Amount));
        return ActiveState.Instance;
    }

    public IInvestmentState Reject(Investment investment, string reason)
    {
        investment.RaiseDomainEvent(new InvestmentRejectedEvent(investment.Id, investment.InvestorId, reason));
        return RejectedState.Instance;
    }

    public IInvestmentState Cancel(Investment investment)
    {
        investment.RaiseDomainEvent(new InvestmentCancelledEvent(investment.Id, investment.InvestorId));
        return CancelledState.Instance;
    }

    public static ReservedState Instance { get; } = new();
    private ReservedState() { }
}
