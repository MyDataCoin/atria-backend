using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Dtos;

/// <summary>
/// Result of starting a KYC verification. The client MUST redirect the user to
/// <see cref="VerificationUrl"/> to complete the hosted provider flow (e.g. Didit). The
/// final decision arrives asynchronously via the provider webhook and is reflected by
/// <c>GET /kyc/me</c>.
/// </summary>
/// <param name="ProfileId">The KYC profile identifier.</param>
/// <param name="Status">Profile status right after submission (UnderReview).</param>
/// <param name="SessionId">The provider verification session id.</param>
/// <param name="VerificationUrl">Hosted URL to redirect the user to in order to complete verification.</param>
public sealed record KycSubmissionDto(Guid ProfileId, KycStatus Status, string SessionId, string VerificationUrl);
