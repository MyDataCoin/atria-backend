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

    // There is deliberately no fixed development code and no outage "magic code" here. Both were
    // authentication bypasses that accepted a known value for any phone, and a bypass that only a
    // configuration flag stands between you and production is one bad deploy away from being live.
    // Tests stub ISmsSender and read the real generated code instead.
}
