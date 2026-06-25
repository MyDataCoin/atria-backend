using Atria.Domain.Applications.Events;
using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>Submitted for review. Can be approved, rejected, or moved to manual review.</summary>
public sealed class SubmittedState : IApplicationState
{
    /// <summary>Stateless singleton (no per-application data lives in the state).</summary>
    public static readonly SubmittedState Instance = new();

    public ApplicationStatus Status => ApplicationStatus.Submitted;

    public IApplicationState Submit(InvestorApplication application)
        => throw new InvalidStateTransitionException("Application has already been submitted.");

    public IApplicationState Approve(InvestorApplication application)
    {
        application.RaiseDomainEvent(new ApplicationApprovedEvent(
            application.Id, application.PropertyId, application.InvestorId, application.Amount));
        return ApprovedState.Instance;
    }

    public IApplicationState Reject(InvestorApplication application, string reason)
    {
        application.SetRejectionReason(reason);
        application.RaiseDomainEvent(new ApplicationRejectedEvent(
            application.Id, application.InvestorId, reason));
        return RejectedState.Instance;
    }

    public IApplicationState MoveToReview(InvestorApplication application)
        => UnderReviewState.Instance;
}
