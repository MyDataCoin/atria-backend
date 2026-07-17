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
        // Identity claims: phone-only accounts, so the identifier claim is the phone number.
        var access = jwt.GenerateAccessToken(user.Id, user.PhoneNumber ?? string.Empty, user.Role);
        var refresh = jwt.GenerateRefreshToken();

        // Store with the refresh token's OWN lifetime (RefreshTokenDays), not the access TTL.
        await refreshTokens.StoreAsync(user.Id, refresh.Token, refresh.ExpiresAtUtc, ct);

        // Persist the refresh token (and any pending changes from the caller, e.g. a
        // revoked old token or a newly created user) so rotation actually works.
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token);
    }

    /// <summary>
    /// Issues a token pair for a credential-login role (Admin/Realtor/SuperAdmin). The password is
    /// authoritatively checked against the seeded <c>users</c> row's hash when one exists (so a
    /// super-admin password reset takes effect and the stale config password stops working); when no
    /// row/hash is seeded yet it falls back to <paramref name="configValidates"/> (the static config
    /// check). A banned account, or any failed check, is refused with a generic 401. On the first
    /// successful config-password login the service account is self-provisioned as a <c>users</c> row
    /// (hash backfilled), so no manual SQL or startup seeding is required. The token carries
    /// <paramref name="role"/> and uses <paramref name="username"/> as the identifier claim.
    /// </summary>
    public static async Task<Result<AuthTokensDto>> IssueForCredentialLoginAsync(
        Guid userId,
        Role role,
        string username,
        string password,
        bool configValidates,
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenStore refreshTokens,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var invalid = Error.Unauthorized("auth.invalid_credentials", "Invalid username or password.");

        var user = await users.GetByIdAsync(userId, ct);
        if (user is not null && user.IsBanned)
            return Result.Failure<AuthTokensDto>(invalid);

        // A stored hash is authoritative; without one (not seeded yet) trust the config check.
        var credentialsOk = user?.PasswordHash is { } hash
            ? passwordHasher.Verify(password, hash)
            : configValidates;

        if (!credentialsOk)
            return Result.Failure<AuthTokensDto>(invalid);

        // Self-provision the service account on first successful login so it exists as a real users
        // row (needed for ban/password operations) WITHOUT any manual SQL or startup seeding. The
        // repository creates it when absent (and backfills a hash for a hand-inserted row), tolerating
        // a concurrent login racing the same insert. Only needed on the config-fallback path — when a
        // hash already matched, the row is obviously present.
        if (user is null || user.PasswordHash is null)
            await users.EnsureServiceAccountAsync(userId, role, passwordHasher.Hash(password), ct);

        var access = jwt.GenerateAccessToken(userId, username, role);
        var refresh = jwt.GenerateRefreshToken();
        await refreshTokens.StoreAsync(userId, refresh.Token, refresh.ExpiresAtUtc, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token));
    }
}
