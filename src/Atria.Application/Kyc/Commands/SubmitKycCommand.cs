using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;
using Atria.Domain.Kyc;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// The current investor submits KYC: ensures a profile exists, opens a provider
/// verification session via the matching Strategy, then moves the profile to
/// UnderReview. Returns the hosted verification URL the client redirects the user to.
/// </summary>
public sealed record SubmitKycCommand(
    KycProviderType Provider,
    string? WalletAddress,
    string? FullName,
    string? DocumentNumber,
    string? Nationality) : IRequest<Result<KycSubmissionDto>>;
