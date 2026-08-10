using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Phone OTP service: rate-limited issuance (per phone AND per address), peppered single-use
/// code hashes, attempt lockout, and constant-time verification. The plaintext code never leaves
/// this method scope, is never returned, and is never logged; phone numbers are masked in logs.
/// </summary>
public sealed class OtpService : IOtpService
{
    // ─── TEMPORARY OTP STUB ───────────────────────────────────────────────────────────────────
    // The SMS gateway must not be called at all right now, so the phone flow runs on a fixed code:
    //   • RequestAsync generates nothing, stores nothing, sends nothing — it just succeeds.
    //   • VerifyAsync accepts StubCode for ANY phone number.
    // This IS an authentication bypass: anyone who knows the code owns any phone number, and the
    // rate limits, attempt lockout and single-use guarantees below do not apply while it is on.
    // Set StubEnabled = false (and re-register the SMS adapter in DependencyInjection) to restore
    // the real flow — nothing else needs to change.
    private const bool StubEnabled = true;
    private const string StubCode = "111111";
    // ──────────────────────────────────────────────────────────────────────────────────────────

    private readonly IOtpCodeStore _store;
    private readonly ISmsSender _sms;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<OtpService> _logger;
    private readonly OtpOptions _options;

    public OtpService(
        IOtpCodeStore store,
        ISmsSender sms,
        IDateTimeProvider clock,
        IUnitOfWork unitOfWork,
        ILogger<OtpService> logger,
        IOptions<OtpOptions> options)
    {
        _store = store;
        _sms = sms;
        _clock = clock;
        _unitOfWork = unitOfWork;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<Result> RequestAsync(string phone, string? ipAddress, CancellationToken ct)
    {
        // Stub mode: return success without touching the store or the SMS gateway. The client
        // already knows the code, so there is nothing to issue and nothing to deliver.
        if (StubEnabled)
        {
            _logger.LogWarning(
                "OTP stub is enabled — no code was issued and no SMS was sent for {Phone}.",
                Mask(phone));
            return Result.Success();
        }

        var now = _clock.UtcNow;
        var sinceUtc = now.AddHours(-1);

        var rateLimited = Result.Failure(Error.Conflict(
            "otp.rate_limited", "Too many OTP requests. Please try again later."));

        // Cap 1 — per phone. Stops someone spamming one victim's handset.
        var recentRequests = await _store.CountRequestsSinceAsync(phone, sinceUtc, ct);
        if (recentRequests >= _options.RequestsPerHour)
            return rateLimited;

        // Cap 2 — per address. The per-phone cap says nothing about a caller walking a list of
        // numbers: five codes each, no phone limit tripped, and the SMS bill is ours. The count of
        // DISTINCT numbers is what separates a person retrying their own login from enumeration.
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            var (requestsFromIp, distinctPhones) =
                await _store.CountRequestsFromIpSinceAsync(ipAddress, sinceUtc, ct);

            if (requestsFromIp >= _options.RequestsPerHourPerIp ||
                distinctPhones >= _options.DistinctPhonesPerHourPerIp)
            {
                _logger.LogWarning(
                    "OTP issuance refused for {Phone}: {Requests} request(s) across {DistinctPhones} "
                    + "number(s) from {Ip} in the last hour.",
                    Mask(phone), requestsFromIp, distinctPhones, ipAddress);
                return rateLimited;
            }
        }

        var code = GenerateNumericCode(_options.Length);
        var codeHash = HashCode(phone, code);
        var expiresAtUtc = now.AddMinutes(_options.TtlMinutes);

        await _store.AddAsync(phone, codeHash, expiresAtUtc, ipAddress, ct);
        // Commit the code BEFORE sending the SMS, otherwise verification can't find it.
        await _unitOfWork.SaveChangesAsync(ct);

        // The message embeds the code; we never log this string.
        var message = $"Добро пожаловать в Atria! Ваш код потверждения: {code}. Он истечёт через {_options.TtlMinutes} минут.";

        try
        {
            await _sms.SendAsync(phone, message, ct);
        }
        catch (SmsGatewayException ex)
        {
            // Log the full exception (stack trace included); the request CorrelationId is attached
            // automatically via the Serilog LogContext. Surface a precise 502 carrying the smspro
            // status code instead of an opaque 500 "An unexpected error occurred".
            _logger.LogError(
                ex, "OTP SMS delivery failed for {Phone}. GatewayStatus={GatewayStatus}",
                Mask(phone), ex.GatewayStatus?.ToString() ?? "<none>");
            return Result.Failure(Error.ExternalService(
                "sms.gateway_error",
                ex.GatewayStatus is { } gatewayStatus
                    ? $"SMS gateway rejected the request (smspro status={gatewayStatus})."
                    : "SMS gateway is currently unavailable. Please try again later."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any other unexpected failure while contacting the gateway: still a downstream
            // problem, not a server bug — return 502 rather than letting it become a generic 500.
            _logger.LogError(ex, "OTP SMS delivery failed unexpectedly for {Phone}.", Mask(phone));
            return Result.Failure(Error.ExternalService(
                "sms.gateway_unreachable",
                "SMS gateway is currently unavailable. Please try again later."));
        }

        return Result.Success();
    }

    public async Task<Result> VerifyAsync(string phone, string code, CancellationToken ct)
    {
        // Stub mode: the fixed code is the only accepted value; no stored code exists to consume.
        if (StubEnabled)
        {
            return code == StubCode
                ? Result.Success()
                : Result.Failure(Error.Validation(
                    "otp.invalid", "The verification code is invalid or has expired."));
        }

        var now = _clock.UtcNow;

        // Every verification goes through the stored, hashed, single-use code. There is no bypass
        // path here on purpose: one accepted for any phone is an unauthenticated account takeover.

        var entry = await _store.GetLatestActiveAsync(phone, ct);
        if (entry is null || entry.Consumed || entry.ExpiresAtUtc <= now)
            return Result.Failure(Error.Validation(
                "otp.invalid", "The verification code is invalid or has expired."));

        if (entry.Attempts >= _options.MaxAttempts)
            return Result.Failure(Error.Conflict(
                "otp.locked", "Too many incorrect attempts. Please request a new code."));

        if (!VerifyCode(phone, code, entry.CodeHash))
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

    /// <summary>
    /// Hashes a code as <c>HMAC-SHA256(pepper, phone | code)</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deliberately is NOT a slow KDF. BCrypt at cost 12 was ~250 ms of CPU per call on two
    /// anonymous endpoints, which turned request-otp and verify-otp into a way to spend the server's
    /// processor from the outside — one request, a quarter second of work, no authentication needed.
    /// </para>
    /// <para>
    /// A slow hash was buying nothing anyway. What protects a six-digit code is the attempt limit and
    /// the five-minute expiry, not the cost of one guess; and against an offline attacker holding the
    /// table, cost 12 over a million-value keyspace is hours, not safety. The pepper is what actually
    /// helps there — it is not in the database, so a dump alone does not reverse the codes.
    /// </para>
    /// <para>
    /// The phone is bound into the hash so a row cannot be replayed against a different number.
    /// </para>
    /// </remarks>
    private string HashCode(string phone, string code)
    {
        var pepper = Convert.FromBase64String(_options.HashPepper);
        var message = Encoding.UTF8.GetBytes($"{phone}|{code}");
        return Convert.ToHexString(HMACSHA256.HashData(pepper, message));
    }

    /// <summary>Constant-time comparison of a candidate code against the stored hash.</summary>
    private bool VerifyCode(string phone, string code, string storedHash)
    {
        byte[] stored;
        try
        {
            stored = Convert.FromHexString(storedHash);
        }
        catch (FormatException)
        {
            // A row written by an older scheme cannot be verified under this one; fail closed.
            return false;
        }

        var candidate = Convert.FromHexString(HashCode(phone, code));
        return CryptographicOperations.FixedTimeEquals(stored, candidate);
    }

    /// <summary>
    /// Masks a phone number for logging: <c>+996700123456</c> becomes <c>+996***456</c>.
    /// </summary>
    /// <remarks>
    /// Full numbers in application logs are personal data sitting outside every control built for
    /// the database — no encryption at rest, no retention rule, no access log of its own.
    /// </remarks>
    private static string Mask(string phone)
        => phone.Length <= 7 ? "***" : $"{phone[..4]}***{phone[^3..]}";
}
