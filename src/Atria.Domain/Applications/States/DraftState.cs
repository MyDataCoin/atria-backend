using Atria.Domain.Applications.Events;
using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>Initial state. Only submission is allowed.</summary>
public sealed class DraftState : IApplicationState
{
    public ApplicationStatus Status => ApplicationStatus.Draft;

    public IApplicationState Submit(InvestorApplication application)
    {
        application.RaiseDomainEvent(new ApplicationSubmittedEvent(
            application.Id, application.InvestorId, application.PropertyId, application.Amount));
        return SubmittedState.Instance;
    }

    public IApplicationState Approve(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot approve an application that was never submitted.");

    public IApplicationState Reject(InvestorApplication application, string reason)
        => throw new InvalidStateTransitionException("Cannot reject a draft application.");

    public IApplicationState MoveToReview(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot move a draft application to review.");
}
