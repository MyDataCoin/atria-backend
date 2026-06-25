using Atria.Domain.Common;

namespace Atria.Domain.Kyc.States;

/// <summary>Terminal state: no further transitions are allowed.</summary>
public sealed class RejectedKycState : IKycState
{
    public KycStatus Status => KycStatus.Rejected;

    public IKycState Submit(KycProfile profile)
        => throw new InvalidStateTransitionException("Cannot resubmit a rejected KYC profile.");

    public IKycState Approve(KycProfile profile)
        => throw new InvalidStateTransitionException("Cannot approve a rejected KYC profile.");

    public IKycState Reject(KycProfile profile, string reason)
        => throw new InvalidStateTransitionException("KYC profile is already rejected.");
}
