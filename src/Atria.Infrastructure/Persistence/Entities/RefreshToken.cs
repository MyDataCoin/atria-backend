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
    public DateTime CreatedAtUtc { get; set; }
}
