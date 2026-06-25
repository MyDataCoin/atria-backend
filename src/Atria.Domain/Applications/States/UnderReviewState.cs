using Atria.Domain.Applications.Events;
using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>Under manual review. Can be approved or rejected.</summary>
public sealed class UnderReviewState : IApplicationState
{
    /// <summary>Stateless singleton (no per-application data lives in the state).</summary>
    public static readonly UnderReviewState Instance = new();

    public ApplicationStatus Status => ApplicationStatus.UnderReview;

    public IApplicationState Submit(InvestorApplication application)
        => throw new InvalidStateTransitionException("Application is already under review.");

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
        => throw new InvalidStateTransitionException("Application is already under review.");
}
