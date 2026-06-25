using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Registers a new email/password Investor account and returns a token pair.</summary>
public sealed record RegisterCommand(string Email, string Password, string? FirstName, string? LastName)
    : IRequest<Result<AuthTokensDto>>;
