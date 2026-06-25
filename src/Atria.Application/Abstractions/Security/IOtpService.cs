using Atria.Application.Common;

namespace Atria.Application.Abstractions;

/// <summary>
/// Phone OTP for registration. Codes are short-lived, single-use, stored hashed,
/// rate-limited per phone/IP, and locked out after too many attempts. The code
/// itself never leaves this service or reaches the logs.
/// </summary>
public interface IOtpService
{
    /// <summary>Generate, hash, store and send a code via SMS. Rate-limited.</summary>
    Task<Result> RequestAsync(string phone, string? ipAddress, CancellationToken ct);

    /// <summary>Constant-time verify; consumes the code on success.</summary>
    Task<Result> VerifyAsync(string phone, string code, CancellationToken ct);
}
