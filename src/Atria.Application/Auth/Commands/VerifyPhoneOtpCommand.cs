using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Verifies the SMS code; creates the Investor account on first use and returns tokens.</summary>
public sealed record VerifyPhoneOtpCommand(string Phone, string Code)
    : IRequest<Result<AuthTokensDto>>;
