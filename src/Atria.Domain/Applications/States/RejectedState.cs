using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>Terminal state. No further transitions are allowed.</summary>
public sealed class RejectedState : IApplicationState
{
    /// <summary>Stateless singleton (the rejection reason lives on the entity, not here).</summary>
    public static readonly RejectedState Instance = new();

    public ApplicationStatus Status => ApplicationStatus.Rejected;

    public IApplicationState Submit(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot submit a rejected application.");

    public IApplicationState Approve(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot approve a rejected application.");

    public IApplicationState Reject(InvestorApplication application, string reason)
        => throw new InvalidStateTransitionException("Application is already rejected.");

    public IApplicationState MoveToReview(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot review a rejected application.");
}
