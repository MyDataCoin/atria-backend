using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Auth.Dtos;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Commands;

/// <summary>
/// Changes the signed-in credential account's own password. This is the flow behind the forced
/// change on first sign-in: a super admin hands over a one-time password, the account is flagged
/// <c>MustResetPassword</c>, and this command is the only way to clear that flag.
/// </summary>
/// <param name="CurrentPassword">The password used to sign in — the one being replaced.</param>
/// <param name="NewPassword">The replacement; must satisfy <see cref="PasswordPolicy"/>.</param>
public sealed record ChangeOwnPasswordCommand(string CurrentPassword, string NewPassword)
    : IRequest<Result<AuthTokensDto>>;

/// <summary>
/// Verifies the current password, stores the new hash, clears the forced-reset flag, and issues a
/// fresh token pair.
/// </summary>
/// <remarks>
/// New tokens are not a convenience: setting a password rotates the account's security stamp, so
/// every token issued under the old one — including the caller's — stops validating on the next
/// request. Handing back a pair is what keeps the change from logging the person out mid-flow.
/// Refresh tokens issued earlier are revoked, so a session opened with the temporary password
/// cannot outlive it.
/// </remarks>
public sealed class ChangeOwnPasswordCommandHandler
    : IRequestHandler<ChangeOwnPasswordCommand, Result<AuthTokensDto>>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeOwnPasswordCommandHandler(
        ICurrentUserService currentUser,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _users = users;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<AuthTokensDto>> Handle(ChangeOwnPasswordCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.unauthenticated", "Not authenticated."));

        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure<AuthTokensDto>(Error.NotFound("user.not_found", "User not found."));

        if (user.Role is not (Role.Admin or Role.Realtor or Role.SuperAdmin) || user.PasswordHash is null)
            return Result.Failure<AuthTokensDto>(Error.Conflict(
                "user.no_password", "Only admin and realtor accounts have a password."));

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            return Result.Failure<AuthTokensDto>(
                Error.Unauthorized("auth.invalid_credentials", "The current password is wrong."));

        // Re-setting the same password would clear the forced-reset flag while leaving the account
        // on the credential a third party (the super admin) knows.
        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            return Result.Failure<AuthTokensDto>(Error.Conflict(
                "auth.password_reused", "The new password must differ from the current one."));

        user.SetPassword(_passwordHasher.Hash(request.NewPassword), mustReset: false);
        _users.Update(user);

        // Sessions opened with the old password (the temporary one, in the first-sign-in case) end here.
        await _refreshTokens.RevokeAllForUserAsync(user.Id, ct);

        await _audit.WriteAsync(
            AuditEntities.User, user.Id, AuditEvents.PasswordChanged,
            $"Пароль изменён владельцем аккаунта ({user.Role})", AuditSeverity.Success, ct);

        return Result.Success(await AuthTokensFactory.IssueAsync(user, _jwt, _refreshTokens, _unitOfWork, ct));
    }
}
