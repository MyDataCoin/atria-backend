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

    /// <summary>
    /// Atomically revokes the token, returning whether THIS caller was the one that revoked it.
    /// </summary>
    /// <remarks>
    /// Rotation is a compare-and-set, not a read-then-write. Two requests presenting the same token
    /// concurrently both pass a "is it revoked?" read and both mint a new pair, leaving two live
    /// sessions from one token — precisely the situation reuse detection exists to catch.
    /// </remarks>
    Task<bool> TryRevokeAsync(string refreshToken, CancellationToken ct);

    /// <summary>Deletes tokens that expired before <paramref name="olderThanUtc"/>. Returns the row count.</summary>
    Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken ct);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
