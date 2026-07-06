using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;
using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Queries;

/// <summary>Handles <see cref="GetKycStatusQuery"/> — reads only the caller's own profile.</summary>
public sealed class GetKycStatusQueryHandler
    : IRequestHandler<GetKycStatusQuery, Result<KycStatusDto>>
{
    private readonly IKycRepository _kyc;
    private readonly ICurrentUserService _currentUser;

    public GetKycStatusQueryHandler(IKycRepository kyc, ICurrentUserService currentUser)
    {
        _kyc = kyc;
        _currentUser = currentUser;
    }

    public async Task<Result<KycStatusDto>> Handle(GetKycStatusQuery request, CancellationToken ct)
    {
        // Resource owner: the caller may only read their OWN KYC profile.
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<KycStatusDto>(
                Error.Unauthorized("Kyc.Unauthorized", "Authentication required."));

        var profile = await _kyc.GetByUserIdAsync(userId, ct);
        if (profile is null)
            return Result.Failure<KycStatusDto>(
                Error.NotFound("Kyc.NotFound", "No KYC profile exists for this user."));

        // Resume support: while a verification is still in progress (UnderReview) but not yet
        // decided, surface the provider session/URL so the client can reopen the same hosted
        // flow the user abandoned. Once terminal (Approved/Rejected) there is nothing to resume.
        var resumable = profile.Status == KycStatus.UnderReview;

        // FullName is decrypted transparently by the EF value converter on read (same
        // IEncryptionService/key used to encrypt at submit); the row stays encrypted at rest.
        // Safe to return here — the handler already scoped the read to the caller's OWN profile.
        return new KycStatusDto(
            profile.Id,
            profile.Status,
            profile.RejectionReason,
            resumable ? profile.ProviderSessionId : null,
            resumable ? profile.VerificationUrl : null,
            profile.FullName);
    }
}
