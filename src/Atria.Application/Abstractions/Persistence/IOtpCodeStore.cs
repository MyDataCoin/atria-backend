namespace Atria.Application.Abstractions;

public sealed record OtpEntry(Guid Id, string CodeHash, DateTime ExpiresAtUtc, int Attempts, bool Consumed);

/// <summary>
/// Persistence for phone OTP codes. Codes are stored ONLY as hashes; the plaintext
/// never touches the database or logs. Backs <see cref="IOtpService"/>.
/// </summary>
public interface IOtpCodeStore
{
    Task AddAsync(string phone, string codeHash, DateTime expiresAtUtc, CancellationToken ct);

    /// <summary>Latest not-consumed, not-expired entry for the phone, or null.</summary>
    Task<OtpEntry?> GetLatestActiveAsync(string phone, CancellationToken ct);

    Task IncrementAttemptsAsync(Guid id, CancellationToken ct);
    Task ConsumeAsync(Guid id, CancellationToken ct);

    /// <summary>How many codes were requested for this phone since <paramref name="sinceUtc"/> (rate limiting).</summary>
    Task<int> CountRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken ct);
}
