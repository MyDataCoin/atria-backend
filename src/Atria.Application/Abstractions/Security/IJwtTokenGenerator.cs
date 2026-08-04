using Atria.Domain.Users;

namespace Atria.Application.Abstractions;

public sealed record AccessToken(string Token, DateTime ExpiresAtUtc);

/// <summary>An opaque refresh token plus its own (longer) lifetime.</summary>
public sealed record GeneratedRefreshToken(string Token, DateTime ExpiresAtUtc);

/// <summary>
/// Issues short-lived access tokens and opaque refresh tokens. The backend keeps
/// no blockchain keys; this is only for application JWTs.
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// Issues an access token for the account. The <paramref name="securityStamp"/> is carried as a
    /// claim and re-checked on every request, so a ban or a password change invalidates tokens already
    /// issued instead of leaving them live for the rest of their lifetime.
    /// </summary>
    AccessToken GenerateAccessToken(Guid userId, Role role, string securityStamp);

    /// <summary>Generates a refresh token carrying its configured expiry (RefreshTokenDays).</summary>
    GeneratedRefreshToken GenerateRefreshToken();
}
