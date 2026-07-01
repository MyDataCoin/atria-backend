using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Dtos;

/// <summary>Read model of a user's KYC profile state.</summary>
/// <param name="Id">The KYC profile identifier.</param>
/// <param name="Status">Current lifecycle status of the profile (e.g. Pending, UnderReview, Approved, Rejected).</param>
/// <param name="RejectionReason">The reason supplied when the profile was rejected; <c>null</c> otherwise.</param>
/// <param name="SessionId">The provider session id of an unfinished verification; <c>null</c> once completed.</param>
/// <param name="VerificationUrl">Hosted provider URL to RESUME an unfinished verification; <c>null</c> once completed.</param>
public sealed record KycStatusDto(
    Guid Id,
    KycStatus Status,
    string? RejectionReason,
    string? SessionId,
    string? VerificationUrl);
