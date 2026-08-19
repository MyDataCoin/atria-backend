namespace Atria.Infrastructure.Persistence;

/// <summary>
/// Persisted refresh token (infra-only EF entity). Only the SHA-256 hash of the
/// raw token is stored; the plaintext never touches the database.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When the token was revoked, or null while it is live.
    /// </summary>
    /// <remarks>
    /// Rotation needs the instant, not just the flag: a token presented again moments after it was
    /// rotated away is a race between two tabs or two parallel calls, while the same token presented
    /// an hour later is a leak. Without the timestamp the two are indistinguishable and every race
    /// ends in a full session revocation — see <c>IRefreshRotationPolicy</c>.
    /// </remarks>
    public DateTime? RevokedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
