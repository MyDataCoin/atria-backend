using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Kyc.Dtos;

namespace Atria.Application.Kyc.Queries;

/// <summary>Returns the current user's KYC profile state.</summary>
public sealed record GetKycStatusQuery : IRequest<Result<KycStatusDto>>;
