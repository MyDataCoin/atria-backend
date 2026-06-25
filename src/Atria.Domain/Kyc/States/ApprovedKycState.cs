using Atria.Domain.Common;

namespace Atria.Domain.Kyc.States;

/// <summary>Terminal state: no further transitions are allowed.</summary>
public sealed class ApprovedKycState : IKycState
{
    public KycStatus Status => KycStatus.Approved;

    public IKycState Submit(KycProfile profile)
        => throw new InvalidStateTransitionException("KYC profile is already approved.");

    public IKycState Approve(KycProfile profile)
        => throw new InvalidStateTransitionException("KYC profile is already approved.");

    public IKycState Reject(KycProfile profile, string reason)
        => throw new InvalidStateTransitionException("Cannot reject an approved KYC profile.");
}
