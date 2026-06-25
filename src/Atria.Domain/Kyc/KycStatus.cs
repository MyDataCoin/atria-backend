namespace Atria.Domain.Kyc;

/// <summary>Current KYC lifecycle status. Driven by the State pattern.</summary>
public enum KycStatus
{
    Pending = 0,
    UnderReview = 1,
    Approved = 2,
    Rejected = 3
}
