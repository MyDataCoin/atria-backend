using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs an admin in with the static username/password and returns a token pair.</summary>
public sealed record AdminLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Verifies credentials for the shared admin login endpoint and, on success, issues a token pair.
/// The SAME endpoint serves the super admin: its credentials are tried FIRST (so a super login is
/// issued a <see cref="Role.SuperAdmin"/> token), then the regular admin. Credentials are checked
/// against the seeded <c>users</c> row's password hash when present (so a super-admin password reset
/// takes effect), falling back to the static config check for a not-yet-seeded stack. A banned
/// account is refused. Returns a generic 401 for a disabled feature, bad credentials, or a ban so
/// nothing is leaked.
/// </summary>
public sealed class AdminLoginCommandHandler : IRequestHandler<AdminLoginCommand, Result<AuthTokensDto>>
{
    private readonly IAdminAuthenticator _admin;
    private readonly ISuperAdminAuthenticator _superAdmin;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public AdminLoginCommandHandler(
        IAdminAuthenticator admin,
        ISuperAdminAuthenticator superAdmin,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _admin = admin;
        _superAdmin = superAdmin;
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(AdminLoginCommand request, CancellationToken ct)
    {
        // Route by USERNAME (super admin shares this endpoint; matched first) — not by password, so a
        // reset password (which no longer matches config) still routes to the right identity. The
        // password itself is verified against the stored hash in the factory, falling back to config.
        var (userId, role, configValidates) =
            _superAdmin.MatchesUsername(request.Username)
                ? (_superAdmin.SuperAdminUserId, Role.SuperAdmin,
                   _superAdmin.Validate(request.Username, request.Password))
                : _admin.MatchesUsername(request.Username)
                    ? (_admin.AdminUserId, Role.Admin,
                       _admin.Validate(request.Username, request.Password))
                    : (Guid.Empty, default(Role), false);

        if (userId == Guid.Empty)
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        return await AuthTokensFactory.IssueForCredentialLoginAsync(
            userId, role, request.Username, request.Password, configValidates,
            _users, _passwordHasher, _jwt, _refreshTokens, _unitOfWork, ct);
    }
}
