namespace Atria.Application.Abstractions;

/// <param name="FamilyId">
/// The sign-in chain this token belongs to. Rotation keeps it; a new sign-in starts a new one. Reuse
/// detection revokes this family alone, so one bad token does not end the user's other sessions.
/// </param>
public sealed record RefreshTokenInfo(
    Guid UserId, string Token, DateTime ExpiresAtUtc, bool IsRevoked, DateTime? RevokedAtUtc = null,
    Guid FamilyId = default);

/// <summary>
/// Stores hashed refresh tokens for rotation + reuse detection. Tokens are grouped into families —
/// one per sign-in — and reuse of a long-revoked token revokes its family (see auth handlers).
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Persists a token. <paramref name="familyId"/> is the sign-in chain it belongs to — pass the
    /// presented token's family when rotating, or a new Guid for a fresh sign-in.
    /// </summary>
    Task StoreAsync(
        Guid userId, string refreshToken, DateTime expiresAtUtc, Guid familyId, CancellationToken ct);
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
    /// <summary>
    /// Revokes every live token in one sign-in chain. This is the reuse-detection response: the chain
    /// the suspect token belongs to is finished, and the user's other devices are untouched.
    /// </summary>
    Task RevokeFamilyAsync(Guid familyId, CancellationToken ct);

    /// <summary>Revokes every live token the user has. Sign-out-everywhere, and account-level events.</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);
}
