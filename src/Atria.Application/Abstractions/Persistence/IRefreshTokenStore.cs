namespace Atria.Application.Abstractions;

public sealed record RefreshTokenInfo(Guid UserId, string Token, DateTime ExpiresAtUtc, bool IsRevoked);

/// <summary>
/// Stores hashed refresh tokens for rotation + reuse detection. Reuse of a
/// revoked token must revoke the whole user session (see auth handlers).
/// </summary>
public interface IRefreshTokenStore
{
    Task StoreAsync(Guid userId, string refreshToken, DateTime expiresAtUtc, CancellationToken ct);
    Task<RefreshTokenInfo?> FindAsync(string refreshToken, CancellationToken ct);
    Task RevokeAsync(string refreshToken, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
