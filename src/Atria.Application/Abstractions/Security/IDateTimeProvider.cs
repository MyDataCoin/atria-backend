namespace Atria.Application.Abstractions;

/// <summary>Abstracts the clock so time-dependent logic (OTP TTL, token expiry) is testable.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
