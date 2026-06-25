using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>Terminal state. No further transitions are allowed.</summary>
public sealed class ApprovedState : IApplicationState
{
    /// <summary>Stateless singleton.</summary>
    public static readonly ApprovedState Instance = new();

    public ApplicationStatus Status => ApplicationStatus.Approved;

    public IApplicationState Submit(InvestorApplication application)
        => throw new InvalidStateTransitionException("Application is already approved.");

    public IApplicationState Approve(InvestorApplication application)
        => throw new InvalidStateTransitionException("Application is already approved.");

    public IApplicationState Reject(InvestorApplication application, string reason)
        => throw new InvalidStateTransitionException("Cannot reject an approved application.");

    public IApplicationState MoveToReview(InvestorApplication application)
        => throw new InvalidStateTransitionException("Cannot review an approved application.");
}
