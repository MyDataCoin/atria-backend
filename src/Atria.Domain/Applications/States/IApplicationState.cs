namespace Atria.Domain.Applications.States;

/// <summary>
/// State-pattern contract for an <see cref="InvestorApplication"/>. EF-friendly variant:
/// states are stateless singletons selected from the persisted status enum; the
/// returned state carries the next <see cref="Status"/>. Illegal transitions throw
/// <see cref="Atria.Domain.Common.InvalidStateTransitionException"/>.
/// </summary>
public interface IApplicationState
{
    /// <summary>The status this state represents.</summary>
    ApplicationStatus Status { get; }

    /// <summary>Submit a draft application for review.</summary>
    IApplicationState Submit(InvestorApplication application);

    /// <summary>Approve a submitted / under-review application.</summary>
    IApplicationState Approve(InvestorApplication application);

    /// <summary>Reject a submitted / under-review application with a reason.</summary>
    IApplicationState Reject(InvestorApplication application, string reason);

    /// <summary>Move a submitted application into manual review.</summary>
    IApplicationState MoveToReview(InvestorApplication application);
}
