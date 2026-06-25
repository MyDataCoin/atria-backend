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
    AccessToken GenerateAccessToken(Guid userId, string email, Role role);

    /// <summary>Generates a refresh token carrying its configured expiry (RefreshTokenDays).</summary>
    GeneratedRefreshToken GenerateRefreshToken();
}
