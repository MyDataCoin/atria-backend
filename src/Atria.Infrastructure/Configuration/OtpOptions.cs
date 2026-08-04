using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>Settings for phone OTP generation, expiry, lockout, and rate limiting.</summary>
public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    /// <summary>Number of numeric digits in a generated code.</summary>
    [Range(4, 10)]
    public int Length { get; init; } = 6;

    /// <summary>How long a code is valid after issuance.</summary>
    [Range(1, 60)]
    public int TtlMinutes { get; init; } = 5;

    /// <summary>Verification attempts allowed before a code is locked.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; init; } = 5;

    /// <summary>Max codes that may be requested per phone within a rolling hour.</summary>
    [Range(1, 100)]
    public int RequestsPerHour { get; init; } = 5;

    /// <summary>Max codes a single address may request within a rolling hour, across all numbers.</summary>
    /// <remarks>
    /// The per-phone cap does not stop the attack that costs money: one caller walking a list of
    /// numbers, a few codes each, never tripping a per-phone limit while the SMS bill grows.
    /// </remarks>
    [Range(1, 1000)]
    public int RequestsPerHourPerIp { get; init; } = 20;

    /// <summary>Max DISTINCT phone numbers a single address may request codes for within a rolling hour.</summary>
    [Range(1, 1000)]
    public int DistinctPhonesPerHourPerIp { get; init; } = 5;

    /// <summary>
    /// Server-side pepper mixed into the stored code hash. Base64 of 32 random bytes.
    /// </summary>
    /// <remarks>
    /// A six-digit code has a million possibilities, so a stolen database of bare hashes is a
    /// one-second offline enumeration. The pepper lives in configuration rather than the row, which
    /// means dumping the table is not enough to reverse the codes. Distinct from Encryption:Key on
    /// purpose — one compromised secret should not hand over two things.
    /// </remarks>
    [Required]
    [Base256BitKey]
    public string HashPepper { get; init; } = string.Empty;

    // There is deliberately no fixed development code and no outage "magic code" here. Both were
    // authentication bypasses that accepted a known value for any phone, and a bypass that only a
    // configuration flag stands between you and production is one bad deploy away from being live.
    // Tests stub ISmsSender and read the real generated code instead.
}
