using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Dtos;

/// <summary>Read model of a user's KYC profile state.</summary>
/// <param name="Id">The KYC profile identifier.</param>
/// <param name="Status">Current lifecycle status of the profile (e.g. Pending, UnderReview, Approved, Rejected).</param>
/// <param name="RejectionReason">The reason supplied when the profile was rejected; <c>null</c> otherwise.</param>
public sealed record KycStatusDto(Guid Id, KycStatus Status, string? RejectionReason);
