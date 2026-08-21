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

    /// <summary>
    /// The sign-in this token descends from. Every token minted by rotating an earlier one inherits
    /// its family; a fresh sign-in starts a new one.
    /// </summary>
    /// <remarks>
    /// This is what makes a compromised token cost one session instead of all of them. Reuse
    /// detection has to invalidate something, and without a family the only thing it could name was
    /// "every token this user has" — so one stale token in one browser tab signed the person out of
    /// their phone, their laptop and everything else at once. The family is exactly the chain the
    /// suspect token belongs to, so the other devices keep working.
    /// </remarks>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
