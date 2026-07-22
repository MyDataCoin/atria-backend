namespace Atria.Domain.Investments.States;

/// <summary>
/// State pattern for an <see cref="Investment"/> (EF-friendly variant). State objects are stateless
/// singletons; the transition methods encapsulate the allowed moves and their side effects (domain
/// events) and return the next state. The mutable path is the offering application flow:
/// Reserved -&gt; Active (operator approves), or Reserved -&gt; Rejected/Cancelled (returns the reserved
/// tokens to the pool). There is no payment step.
/// </summary>
public interface IInvestmentState
{
    InvestmentStatus Status { get; }

    /// <summary>Reserved -&gt; Active: operator approves the application; raises the activation event.</summary>
    IInvestmentState Approve(Investment investment);

    /// <summary>Reserved -&gt; Rejected: operator declines; raises the rejected event (caller returns tokens).</summary>
    IInvestmentState Reject(Investment investment, string reason);

    /// <summary>Reserved -&gt; Cancelled: investor withdraws the application; raises the cancelled event.</summary>
    IInvestmentState Cancel(Investment investment);
}
