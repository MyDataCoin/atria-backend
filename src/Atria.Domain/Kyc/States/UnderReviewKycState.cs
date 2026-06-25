using Atria.Domain.Common;
using Atria.Domain.Kyc.Events;

namespace Atria.Domain.Kyc.States;

/// <summary>Under review by the provider/compliance. Approve or Reject are legal.</summary>
public sealed class UnderReviewKycState : IKycState
{
    public KycStatus Status => KycStatus.UnderReview;

    public IKycState Submit(KycProfile profile)
        => throw new InvalidStateTransitionException("KYC profile has already been submitted.");

    public IKycState Approve(KycProfile profile)
    {
        profile.RaiseDomainEvent(new KycApprovedEvent(profile.Id, profile.UserId, profile.WalletAddress));
        return KycStateFactory.Approved;
    }

    public IKycState Reject(KycProfile profile, string reason)
    {
        profile.RaiseDomainEvent(new KycRejectedEvent(profile.Id, profile.UserId, reason));
        return KycStateFactory.Rejected;
    }
}
