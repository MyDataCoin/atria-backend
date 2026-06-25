using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Kyc.Commands;

/// <summary>
/// A Compliance officer approves or rejects a KYC profile under review.
/// </summary>
public sealed record ReviewKycCommand(Guid KycId, bool Approve, string? Reason) : IRequest<Result>;
