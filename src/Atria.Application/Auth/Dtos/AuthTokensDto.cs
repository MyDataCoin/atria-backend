using Atria.Application.Abstractions;
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
        // Identity claims: email may be null for phone accounts; fall back to phone.
        var access = jwt.GenerateAccessToken(user.Id, user.Email ?? user.PhoneNumber ?? string.Empty, user.Role);
        var refresh = jwt.GenerateRefreshToken();

        // Store with the refresh token's OWN lifetime (RefreshTokenDays), not the access TTL.
        await refreshTokens.StoreAsync(user.Id, refresh.Token, refresh.ExpiresAtUtc, ct);

        // Persist the refresh token (and any pending changes from the caller, e.g. a
        // revoked old token or a newly created user) so rotation actually works.
        await unitOfWork.SaveChangesAsync(ct);

        return new AuthTokensDto(access.Token, access.ExpiresAtUtc, refresh.Token);
    }
}
