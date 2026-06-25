using Atria.Domain.Common;
using Atria.Domain.Kyc.Events;

namespace Atria.Domain.Kyc.States;

/// <summary>Initial state. Only Submit is legal (Pending -> UnderReview).</summary>
public sealed class PendingKycState : IKycState
{
    public KycStatus Status => KycStatus.Pending;

    public IKycState Submit(KycProfile profile)
    {
        profile.RaiseDomainEvent(new KycSubmittedEvent(profile.Id, profile.UserId));
        return KycStateFactory.UnderReview;
    }

    public IKycState Approve(KycProfile profile)
        => throw new InvalidStateTransitionException("Cannot approve a KYC profile that has not been submitted.");

    public IKycState Reject(KycProfile profile, string reason)
        => throw new InvalidStateTransitionException("Cannot reject a KYC profile that has not been submitted.");
}
