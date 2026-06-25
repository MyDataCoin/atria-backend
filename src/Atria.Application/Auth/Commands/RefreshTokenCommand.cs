using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Rotates a refresh token into a fresh access + refresh pair (with reuse detection).</summary>
public sealed record RefreshTokenCommand(string RefreshToken)
    : IRequest<Result<AuthTokensDto>>;
