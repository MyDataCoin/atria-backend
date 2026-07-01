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

    /// <summary>
    /// DEV/TEST ONLY. When set (e.g. "333333"), every OTP is this fixed code and no SMS
    /// is sent — lets you exercise phone registration without an SMS gateway. MUST be
    /// null/empty in production (the service logs a loud warning whenever it is active).
    /// </summary>
    public string? DevFixedCode { get; init; }

    /// <summary>
    /// TEMPORARY OUTAGE BYPASS. When set (e.g. "111111"), verify-otp accepts this exact code
    /// for ANY phone WITHOUT a prior request-otp and WITHOUT contacting the SMS gateway —
    /// used only while the SMS provider is down. This is an auth bypass: keep it null/empty
    /// normally and REMOVE it the moment SMS is restored. The service logs a loud warning on
    /// every use.
    /// </summary>
    public string? MagicCode { get; init; }
}
