using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Trading;

/// <summary>
/// Verifies trade events with an HMAC-SHA256 signature over <c>{timestamp}.{body}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The timestamp is inside the signed string, not merely sent beside it. Signing the body alone
/// would leave a captured request replayable forever, because its signature stays valid for as long
/// as the key does.
/// </para>
/// <para>
/// A secret that is absent, a placeholder, or too short counts as no secret at all. The platform has
/// already been bitten once by a committed <c>CHANGE_ME</c> default that satisfied an emptiness check
/// and then produced perfectly valid signatures under a secret published in the repository; the same
/// mistake here would let anyone file trades against our register.
/// </para>
/// </remarks>
public sealed class HmacExchangeSignatureVerifier : IExchangeSignatureVerifier
{
    /// <summary>Shortest secret worth calling one. Anything below this is a placeholder.</summary>
    private const int MinimumSecretLength = 16;

    private static readonly string[] PlaceholderSecrets =
        ["change_me", "changeme", "secret", "test", "todo", "placeholder"];

    private readonly ExchangeOptions _options;
    private readonly ILogger<HmacExchangeSignatureVerifier> _logger;

    public HmacExchangeSignatureVerifier(
        IOptions<ExchangeOptions> options, ILogger<HmacExchangeSignatureVerifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public SignatureVerdict Verify(string rawBody, string? signature, string? timestamp)
    {
        var secrets = UsableSecrets();
        if (secrets.Count == 0)
        {
            _logger.LogWarning(
                "A trade event arrived but no exchange webhook secret is configured; refusing it.");
            return SignatureVerdict.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(timestamp))
            return SignatureVerdict.Missing;

        if (!TryParseTimestamp(timestamp, out var sentAt))
            return SignatureVerdict.Missing;

        var drift = Math.Abs((DateTimeOffset.UtcNow - sentAt).TotalSeconds);
        if (drift > _options.SignatureToleranceSeconds)
        {
            _logger.LogWarning(
                "Trade event timestamp is {Drift:F0}s from our clock (tolerance {Tolerance}s); "
                + "treating it as a replay.",
                drift, _options.SignatureToleranceSeconds);
            return SignatureVerdict.Stale;
        }

        var signedPayload = $"{timestamp}.{rawBody}";
        var provided = Normalize(signature);

        // Every configured key is tried so that a rotation does not drop events mid-switch. The
        // comparison itself is constant-time: a byte-by-byte early exit leaks how much of a forged
        // signature was correct, which is enough to reconstruct one.
        foreach (var secret in secrets)
        {
            var expected = Compute(secret, signedPayload);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
            {
                return SignatureVerdict.Valid;
            }
        }

        return SignatureVerdict.Invalid;
    }

    private List<string> UsableSecrets()
    {
        var candidates = new[] { _options.WebhookSecret, _options.PreviousWebhookSecret };

        return candidates
            .Where(s => !string.IsNullOrWhiteSpace(s)
                        && s!.Length >= MinimumSecretLength
                        && !PlaceholderSecrets.Contains(s.Trim().ToLowerInvariant()))
            .Select(s => s!)
            .ToList();
    }

    private static string Compute(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Accepts a bare hex digest or one prefixed <c>sha256=</c>, in either case.</summary>
    private static string Normalize(string signature)
    {
        var value = signature.Trim();
        const string prefix = "sha256=";

        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            value = value[prefix.Length..];

        return value.ToLowerInvariant();
    }

    private static bool TryParseTimestamp(string raw, out DateTimeOffset value)
    {
        // Unix seconds is what HMAC schemes overwhelmingly use; ISO-8601 is accepted so an operator
        // sending a readable timestamp is not turned away over formatting.
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            value = DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }

        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }
}
