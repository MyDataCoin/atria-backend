using Atria.Application.Abstractions;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>Logs a realtor in with the static username/password and returns a token pair.</summary>
public sealed record RealtorLoginCommand(string Username, string Password) : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Verifies the realtor credentials and, on success, issues a Realtor token pair. Credentials are
/// checked against the seeded <c>users</c> row's password hash when present (so a super-admin
/// password reset takes effect), falling back to the static config check for a not-yet-seeded stack.
/// A banned account is refused. Returns a generic 401 for a disabled feature, bad credentials, or a
/// ban so nothing is leaked.
/// </summary>
public sealed class RealtorLoginCommandHandler : IRequestHandler<RealtorLoginCommand, Result<AuthTokensDto>>
{
    private readonly IRealtorAuthenticator _realtor;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public RealtorLoginCommandHandler(
        IRealtorAuthenticator realtor,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork)
    {
        _realtor = realtor;
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(RealtorLoginCommand request, CancellationToken ct)
    {
        // Route by username, not password, so a reset password still logs in; the password is
        // verified against the stored hash in the factory, falling back to the config check.
        if (!_realtor.MatchesUsername(request.Username))
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        return await AuthTokensFactory.IssueForCredentialLoginAsync(
            _realtor.RealtorUserId, Role.Realtor, request.Username, request.Password,
            _realtor.Validate(request.Username, request.Password),
            _users, _passwordHasher, _jwt, _refreshTokens, _unitOfWork, ct);
    }
}
