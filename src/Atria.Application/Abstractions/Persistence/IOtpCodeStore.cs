namespace Atria.Application.Abstractions;

public sealed record OtpEntry(Guid Id, string CodeHash, DateTime ExpiresAtUtc, int Attempts, bool Consumed);

/// <summary>
/// Persistence for phone OTP codes. Codes are stored ONLY as hashes; the plaintext
/// never touches the database or logs. Backs <see cref="IOtpService"/>.
/// </summary>
public interface IOtpCodeStore
{
    Task AddAsync(
        string phone, string codeHash, DateTime expiresAtUtc, string? requestedFromIp, CancellationToken ct);

    /// <summary>Latest not-consumed, not-expired entry for the phone, or null.</summary>
    Task<OtpEntry?> GetLatestActiveAsync(string phone, CancellationToken ct);

    Task IncrementAttemptsAsync(Guid id, CancellationToken ct);
    Task ConsumeAsync(Guid id, CancellationToken ct);

    /// <summary>How many codes were requested for this phone since <paramref name="sinceUtc"/> (rate limiting).</summary>
    Task<int> CountRequestsSinceAsync(string phone, DateTime sinceUtc, CancellationToken ct);

    /// <summary>
    /// How many codes this address requested since <paramref name="sinceUtc"/>, and how many
    /// DISTINCT phone numbers it asked for.
    /// </summary>
    /// <remarks>
    /// The per-phone cap alone does not stop the attack that costs real money: one caller walking a
    /// list of numbers, five codes each, burning the SMS budget without ever tripping a per-phone
    /// limit. The count of distinct numbers is what separates a person retrying their own login from
    /// something enumerating the range.
    /// </remarks>
    Task<(int Requests, int DistinctPhones)> CountRequestsFromIpSinceAsync(
        string ipAddress, DateTime sinceUtc, CancellationToken ct);
}
