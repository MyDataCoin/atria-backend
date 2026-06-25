using Atria.Domain.Common;

namespace Atria.Domain.Applications.States;

/// <summary>
/// Stateless factory that rehydrates the current <see cref="IApplicationState"/> from
/// the persisted <see cref="ApplicationStatus"/> enum (EF-friendly State variant).
/// </summary>
public static class ApplicationStateFactory
{
    public static IApplicationState Create(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => new DraftState(),
        ApplicationStatus.Submitted => SubmittedState.Instance,
        ApplicationStatus.UnderReview => UnderReviewState.Instance,
        ApplicationStatus.Approved => ApprovedState.Instance,
        ApplicationStatus.Rejected => RejectedState.Instance,
        _ => throw new InvalidStateTransitionException($"Unknown application status '{status}'.")
    };
}
