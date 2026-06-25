using System.Security.Cryptography;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Phone OTP service: rate-limited issuance, hashed single-use codes, attempt lockout,
/// and constant-time verification. The plaintext code never leaves this method scope,
/// is never returned, and is never logged.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly IOtpCodeStore _store;
    private readonly ISmsSender _sms;
    private readonly IPasswordHasher _hasher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    public OtpService(
        IOtpCodeStore store,
        ISmsSender sms,
        IPasswordHasher hasher,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<OtpService> logger,
        IOptions<OtpOptions> options)
    {
        _store = store;
        _sms = sms;
        _hasher = hasher;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Result> RequestAsync(string phone, string? ipAddress, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // Rate limit: cap codes issued per phone within the rolling hour.
        var sinceUtc = now.AddHours(-1);
        var recentRequests = await _store.CountRequestsSinceAsync(phone, sinceUtc, ct);
        if (recentRequests >= _options.RequestsPerHour)
            return Result.Failure(Error.Conflict(
                "otp.rate_limited", "Too many OTP requests. Please try again later."));

        // DEV/TEST shortcut: when a fixed code is configured, use it and skip the SMS
        // gateway entirely so phone registration can be tested without a provider.
        var devCode = _options.DevFixedCode;
        var devMode = !string.IsNullOrEmpty(devCode);

        var code = devMode ? devCode! : GenerateNumericCode(_options.Length);
        var codeHash = _hasher.Hash(code);
        var expiresAtUtc = now.AddMinutes(_options.TtlMinutes);

        await _store.AddAsync(phone, codeHash, expiresAtUtc, ct);
        // Commit the code BEFORE sending the SMS, otherwise verification can't find it.
        await _unitOfWork.SaveChangesAsync(ct);

        if (devMode)
        {
            // Loud, repeated warning so this can never be mistaken for production behavior.
            // The code value itself is not logged — it lives in configuration.
            _logger.LogWarning(
                "OTP DEV MODE ACTIVE: a fixed development code is in use and NO SMS was sent for {Phone}. " +
                "Set Otp:DevFixedCode to empty in production.", phone);
            return Result.Success();
        }

        // The message embeds the code; we never log this string.
        var message = $"Your Atria verification code is {code}. It expires in {_options.TtlMinutes} minutes.";
        await _sms.SendAsync(phone, message, ct);

        return Result.Success();
    }

    public async Task<Result> VerifyAsync(string phone, string code, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        var entry = await _store.GetLatestActiveAsync(phone, ct);
        if (entry is null || entry.Consumed || entry.ExpiresAtUtc <= now)
            return Result.Failure(Error.Validation(
                "otp.invalid", "The verification code is invalid or has expired."));

        if (entry.Attempts >= _options.MaxAttempts)
            return Result.Failure(Error.Conflict(
                "otp.locked", "Too many incorrect attempts. Please request a new code."));

        // BCrypt.Verify performs a constant-time comparison of the candidate vs stored hash.
        if (!_hasher.Verify(code, entry.CodeHash))
        {
            await _store.IncrementAttemptsAsync(entry.Id, ct);
            // Persist the attempt so the MaxAttempts lockout actually accumulates (anti-brute-force).
            await _unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(Error.Validation(
                "otp.invalid", "The verification code is invalid or has expired."));
        }

        // Consume the code so it is strictly single-use; commit immediately.
        await _store.ConsumeAsync(entry.Id, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>Generate an unbiased numeric code of the given length (leading zeros preserved).</summary>
    private static string GenerateNumericCode(int length)
    {
        var digits = new char[length];
        for (var i = 0; i < length; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(digits);
    }
}
