using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Kyc;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Kyc.Providers;

/// <summary>
/// PRIMARY KYC provider — Didit hosted verification flow.
/// Creates a hosted session via the Didit API, verifies inbound webhooks with an
/// HMAC-SHA256 signature + timestamp freshness (replay protection), and parses the
/// decision payload. Selected through DI by <see cref="ProviderType"/> (Strategy).
/// </summary>
public sealed class DiditKycProvider : IKycProviderStrategy
{
    // NOTE: Didit field/header names per their hosted-session + webhook docs.
    // Adjust these constants if the account uses a different API contract/version.
    private const string SessionEndpoint = "/v2/session/";        // POST -> hosted session
    private const string SignatureHeader = "x-signature";          // hex HMAC-SHA256 of raw body
    private const string TimestampHeader = "x-timestamp";          // unix seconds

    private readonly HttpClient _http;
    private readonly DiditOptions _options;
    private readonly ILogger<DiditKycProvider> _logger;

    public DiditKycProvider(
        HttpClient http,
        IOptions<DiditOptions> options,
        ILogger<DiditKycProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public KycProviderType ProviderType => KycProviderType.Didit;

    public async Task<KycSessionResult> CreateSessionAsync(KycSessionRequest request, CancellationToken ct)
    {
        // NOTE: Didit hosted-session body. "workflow_id"/"features" + a vendor reference
        // we can correlate back to our KycProfile, and the post-verification redirect URL.
        var body = new
        {
            workflow_id = _options.WorkflowId,
            vendor_data = request.KycProfileId.ToString(),
            callback = request.RedirectUrl,
            contact_details = new { email = request.Email }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, SessionEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };
        // Didit authenticates session creation with the API key.
        message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);

        using var response = await _http.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Didit session creation failed with status {Status}", (int)response.StatusCode);
            throw new InvalidOperationException(
                $"Didit session creation failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // NOTE: Didit returns "session_id" and the hosted "url" to redirect the user to.
        var sessionId = GetString(root, "session_id", "id")
            ?? throw new InvalidOperationException("Didit response missing session id.");
        var verificationUrl = GetString(root, "url", "verification_url", "session_url")
            ?? throw new InvalidOperationException("Didit response missing verification url.");

        return new KycSessionResult(sessionId, verificationUrl);
    }

    public bool VerifySignature(WebhookPayload payload)
    {
        // Never trust the body until both signature AND timestamp freshness pass.
        if (string.IsNullOrEmpty(_options.WebhookSecret))
        {
            _logger.LogWarning("Didit webhook secret not configured; rejecting webhook.");
            return false;
        }

        var signature = payload.Signature ?? Header(payload, SignatureHeader);
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        // Replay protection: timestamp must be present and within tolerance.
        if (!TryGetTimestamp(payload, out var sentAt))
            return false;

        var age = DateTimeOffset.UtcNow - sentAt;
        if (Math.Abs(age.TotalSeconds) > _options.WebhookToleranceSeconds)
        {
            _logger.LogWarning("Didit webhook timestamp outside tolerance; possible replay.");
            return false;
        }

        // HMAC-SHA256 over the raw body, hex-encoded, compared in constant time.
        var expected = ComputeHmacHex(payload.RawBody, _options.WebhookSecret);
        var provided = signature.Trim();

        return FixedTimeEqualsHex(expected, provided);
    }

    public KycCallbackResult ParseCallback(WebhookPayload payload)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        // NOTE: Didit webhook shape — "session_id", "status", optional "reason"/"comment",
        // and a unique delivery id ("webhook_id"/"id") used for idempotency.
        var sessionId = GetString(root, "session_id", "vendor_data", "reference")
            ?? throw new InvalidOperationException("Didit webhook missing session id.");

        var status = GetString(root, "status", "decision", "review_result") ?? string.Empty;
        var reason = GetString(root, "reason", "comment", "decline_reason");
        var eventId = GetString(root, "webhook_id", "event_id", "id")
            ?? sessionId; // fall back to session id so the handler can still dedupe

        var decision = MapDecision(status);
        return new KycCallbackResult(sessionId, decision, reason, eventId);
    }

    // NOTE: Didit statuses. "Approved"/"verified" -> Approved; everything terminal-negative -> Declined.
    private static KycDecision MapDecision(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "approved" or "verified" or "success" or "completed" => KycDecision.Approved,
            _ => KycDecision.Declined
        };

    private static bool TryGetTimestamp(WebhookPayload payload, out DateTimeOffset timestamp)
    {
        if (payload.Timestamp is { } t)
        {
            timestamp = t;
            return true;
        }

        var raw = Header(payload, TimestampHeader);
        if (long.TryParse(raw, out var unix))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unix);
            return true;
        }

        timestamp = default;
        return false;
    }

    private static string ComputeHmacHex(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
        return Convert.ToHexString(hash); // upper-case hex
    }

    private static bool FixedTimeEqualsHex(string expectedHex, string providedHex)
    {
        // Strip optional "sha256=" prefix some providers add.
        var idx = providedHex.IndexOf('=');
        if (idx >= 0 && idx < providedHex.Length - 1)
            providedHex = providedHex[(idx + 1)..];

        byte[] expected;
        byte[] provided;
        try
        {
            expected = Convert.FromHexString(expectedHex);
            provided = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    private static string? Header(WebhookPayload payload, string name)
        => payload.Headers.TryGetValue(name, out var v) ? v : null;

    // First matching, non-empty string property among the candidate names.
    private static string? GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var value = prop.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }
}
