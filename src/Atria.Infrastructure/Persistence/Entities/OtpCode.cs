namespace Atria.Infrastructure.Persistence;

/// <summary>
/// Persisted phone OTP code (infra-only EF entity). Only the hash of the code is
/// stored; the plaintext never touches the database or logs.
/// </summary>
public sealed class OtpCode
{
    public Guid Id { get; set; }
    public string Phone { get; set; } = default!;
    public string CodeHash { get; set; } = default!;
    public DateTime ExpiresAtUtc { get; set; }
    public int Attempts { get; set; }
    public bool Consumed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
