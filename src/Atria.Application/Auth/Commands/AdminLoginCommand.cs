using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs a credential account in with a username/password and returns a token pair.</summary>
public sealed record AdminLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Credential login for the shared admin endpoint. Looks the account up by username in the database
/// and verifies the password against its stored hash; the issued token's role (Admin or SuperAdmin)
/// comes from the row, so the same endpoint serves both. No configuration is involved — accounts live
/// only in <c>users</c>. A missing/wrong/banned account yields a generic 401.
/// </summary>
public sealed class AdminLoginCommandHandler : IRequestHandler<AdminLoginCommand, Result<AuthTokensDto>>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public AdminLoginCommandHandler(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public Task<Result<AuthTokensDto>> Handle(AdminLoginCommand request, CancellationToken ct)
        => AuthTokensFactory.IssueForCredentialLoginAsync(
            request.Username, request.Password,
            _users, _passwordHasher, _jwt, _refreshTokens, _unitOfWork, ct);
}
