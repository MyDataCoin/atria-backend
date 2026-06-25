using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Authenticates an email/password account and returns a token pair.</summary>
public sealed record LoginCommand(string Email, string Password)
    : IRequest<Result<AuthTokensDto>>;
