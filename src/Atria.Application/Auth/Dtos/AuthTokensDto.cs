using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Users;

namespace Atria.Application.Auth.Dtos;

/// <summary>Token pair returned by every successful authentication flow.</summary>
/// <param name="AccessToken">The signed JWT access token to send as a Bearer token on subsequent requests.</param>
/// <param name="ExpiresAtUtc">UTC instant at which the access token expires.</param>
/// <param name="RefreshToken">The rotating refresh token; exchange it at <c>auth/refresh</c> for a new pair.</param>
public sealed record AuthTokensDto(string AccessToken, DateTime ExpiresAtUtc, string RefreshToken);

/// <summary>
/// Builds an <see cref="AuthTokensDto"/> for a user: issues a fresh access token +
/// refresh token, persists the (hashed) refresh token for rotation, and COMMITS it.
/// Shared by all auth handlers so the issue-store-commit logic lives in one place —
/// the refresh token must be durable before it is handed to the client, otherwise the
/// next refresh call can't find it.
/// </summary>
internal static class AuthTokensFactory
{
    public static async Task<AuthTokensDto> IssueAsync(
        User user,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        // Identifier claim: a credential account's username, else the phone number (OTP accounts).
        var identifier = user.Username ?? user.PhoneNumber ?? string.Empty;
        var access = jwt.GenerateAccessToken(user.Id, identifier, user.Role);
        var refresh = jwt.GenerateRefreshToken();

        // Store with the refresh token's OWN lifetime (RefreshTokenDays), not the access TTL.
        await refreshTokens.StoreAsync(user.Id, refresh.Token, refresh.ExpiresAtUtc, ct);

        // Persist the refresh token (and any pending changes from the caller, e.g. a
        // revoked old token or a newly created user) so rotation actually works.
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token);
    }

    /// <summary>
    /// Logs a credential account (Admin/Realtor/SuperAdmin) in purely from the database: looks the
    /// account up by <paramref name="username"/>, verifies the password against its stored hash, and
    /// issues a token whose role is taken from the row. There is no configuration involved — accounts
    /// live only in <c>users</c>. A missing account, wrong password, or a banned account all yield the
    /// same generic 401 so nothing is leaked. The account must have a password hash (a credential
    /// account); a row without one (e.g. an investor) is treated as invalid.
    /// </summary>
    public static async Task<Result<AuthTokensDto>> IssueForCredentialLoginAsync(
        string username,
        string password,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var invalid = Result.Failure<AuthTokensDto>(
            Error.Unauthorized("auth.invalid_credentials", "Invalid username or password."));

        var user = await users.GetByUsernameAsync(username, ct);
        if (user is null || user.PasswordHash is null || user.IsBanned)
            return invalid;

        if (!passwordHasher.Verify(password, user.PasswordHash))
            return invalid;

        return Result.Success(await IssueAsync(user, jwt, refreshTokens, unitOfWork, ct));
    }
}
