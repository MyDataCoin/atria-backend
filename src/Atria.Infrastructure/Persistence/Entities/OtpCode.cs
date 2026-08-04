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

    /// <summary>
    /// Address the code was requested from. Feeds the per-IP issuance cap and gives an abuse
    /// investigation something to work with; null when the request arrived without a resolvable peer.
    /// </summary>
    public string? RequestedFromIp { get; set; }
}
