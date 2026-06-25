using Atria.Domain.Common;

namespace Atria.Domain.Kyc.States;

/// <summary>
/// Maps a persisted <see cref="KycStatus"/> back to its stateless state singleton.
/// State objects hold no data, so a single shared instance per status is safe.
/// </summary>
public static class KycStateFactory
{
    public static readonly IKycState Pending = new PendingKycState();
    public static readonly IKycState UnderReview = new UnderReviewKycState();
    public static readonly IKycState Approved = new ApprovedKycState();
    public static readonly IKycState Rejected = new RejectedKycState();

    public static IKycState Create(KycStatus status) => status switch
    {
        KycStatus.Pending => Pending,
        KycStatus.UnderReview => UnderReview,
        KycStatus.Approved => Approved,
        KycStatus.Rejected => Rejected,
        _ => throw new DomainException($"Unknown KYC status: {status}.")
    };
}
