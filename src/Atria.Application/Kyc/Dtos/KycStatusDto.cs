using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Dtos;

/// <summary>Read model of a user's KYC profile state.</summary>
public sealed record KycStatusDto(Guid Id, KycStatus Status, string? RejectionReason);
