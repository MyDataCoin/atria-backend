using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Dtos;

/// <summary>Token pair returned by every successful authentication flow.</summary>
/// <param name="AccessToken">The signed JWT access token to send as a Bearer token on subsequent requests.</param>
/// <param name="ExpiresAtUtc">UTC instant at which the access token expires.</param>
/// <param name="RefreshToken">The rotating refresh token; exchange it at <c>auth/refresh</c> for a new pair.</param>
/// <param name="RefreshExpiresAtUtc">
/// UTC instant at which the REFRESH token expires. Reported separately because it outlives the
/// access token by weeks, and the API needs it to give the refresh cookie the right lifetime —
/// scoping that cookie to the access token's fifteen minutes would log everyone out a quarter of an
/// hour after signing in.
/// </param>
public sealed record AuthTokensDto(
    string AccessToken, DateTime ExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc);

/// <summary>
/// Builds an <see cref="AuthTokensDto"/> for a user: issues a fresh access token +
/// refresh token, persists the (hashed) refresh token for rotation, and COMMITS it.
/// Shared by all auth handlers so the issue-store-commit logic lives in one place —
/// the refresh token must be durable before it is handed to the client, otherwise the
/// next refresh call can't find it.
/// </summary>
internal static class AuthTokensFactory
{

    /// <param name="familyId">
    /// The sign-in chain the new token joins. Pass the presented token's family when rotating, so the
    /// whole chain stays one session; leave it null for a fresh sign-in, which starts its own.
    /// </param>
    public static async Task<AuthTokensDto> IssueAsync(
        User user,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork,
        CancellationToken ct,
        Guid? familyId = null)
    {
        var access = jwt.GenerateAccessToken(user.Id, user.Role, user.SecurityStamp);
        var refresh = jwt.GenerateRefreshToken();

        // Store with the refresh token's OWN lifetime (RefreshTokenDays), not the access TTL.
        await refreshTokens.StoreAsync(
            user.Id, refresh.Token, refresh.ExpiresAtUtc, familyId ?? Guid.NewGuid(), ct);

        // Persist the refresh token (and any pending changes from the caller, e.g. a
        // revoked old token or a newly created user) so rotation actually works.
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token, refresh.ExpiresAtUtc);
    }

    /// <summary>
    /// Logs a credential account (Admin/Realtor/SuperAdmin) in purely from the database: looks the
    /// account up by <paramref name="username"/>, verifies the password against its stored hash, and
    /// issues a token whose role is taken from the row. There is no configuration involved — accounts
    /// live only in <c>users</c>. A missing account, wrong password, or a banned account all yield the
    /// same generic 401 so nothing is leaked. The account must have a password hash (a credential
    /// account); a row without one (e.g. an investor) is treated as invalid.
    /// </summary>
    /// <remarks>
    /// The account itself counts failures and locks per <see cref="IAuthLockoutPolicy"/>. Per-IP
    /// throttling in the pipeline does not cover this case: an attacker with a pool of addresses spends
    /// one attempt per address and never trips it, so the limit that has to hold lives on the row.
    /// </remarks>
    public static async Task<Result<AuthTokensDto>> IssueForCredentialLoginAsync(
        string username,
        string password,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IDateTimeProvider clock,
        IAuthLockoutPolicy lockout,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var invalid = Result.Failure<AuthTokensDto>(
            Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        var now = clock.UtcNow;
        var user = await users.GetByUsernameAsync(username, ct);

        // Unknown account: nothing to count against, and the answer must not differ from a wrong
        // password or the endpoint becomes a username oracle.
        if (user is null || user.PasswordHash is null)
            return invalid;

        // A locked account is refused before the password is even checked — otherwise the lockout
        // still burns a BCrypt verification per attempt and stays a CPU amplifier.
        if (user.IsLockedOut(now))
            return invalid;

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            user.RegisterFailedLogin(now, lockout.MaxFailedLogins, lockout.LockoutDuration);
            users.Update(user);
            await unitOfWork.SaveChangesAsync(ct);
            return invalid;
        }

        // Correct credentials but banned: distinct 403 so the client can show the blocked screen and
        // offer an appeal. A wrong password stays a generic 401 (ban status is not leaked). The ban
        // reason (when set) is carried in the error message so the API surfaces it as banReason/detail.
        if (user.IsBanned)
            return Result.Failure<AuthTokensDto>(Error.AccountBanned(user.BanReason));

        // Only touch the row when there is a streak or a lapsed lock to clear. Writing on every
        // successful sign-in turns the common path into an UPDATE for no gain, and lets two
        // concurrent logins for the same account collide on it.
        if (user.HasLoginFailureState)
        {
            user.RegisterSuccessfulLogin();
            users.Update(user);
        }

        return Result.Success(await IssueAsync(user, jwt, refreshTokens, unitOfWork, ct));
    }
}
