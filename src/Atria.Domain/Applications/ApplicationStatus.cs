namespace Atria.Domain.Applications;

/// <summary>Investor application lifecycle status. Driven by the State pattern.</summary>
public enum ApplicationStatus
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    Approved = 3,
    Rejected = 4
}
