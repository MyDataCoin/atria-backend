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
    // Didit contract (docs.didit.me). Create session: POST /v3/session/ (x-api-key).
    private const string SessionEndpoint = "/v3/session/";          // POST -> hosted session
    private const string SignatureHeader = "X-Signature";          // hex HMAC-SHA256 of the raw body
    private const string TimestampHeader = "X-Timestamp";          // unix epoch seconds
    private const string StatusUpdatedType = "status.updated";      // the session-status event we act on

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

        // Didit webhook envelope: event_id (idempotency), session_id, status, webhook_type.
        // session_id may be absent on non-session events (those are acked as Pending no-ops).
        var webhookType = GetString(root, "webhook_type") ?? string.Empty;
        var sessionId = GetString(root, "session_id") ?? string.Empty;
        var status = GetString(root, "status") ?? string.Empty;
        var reason = GetString(root, "reason", "comment");
        var eventId = GetString(root, "event_id", "webhook_id", "id") ?? sessionId;

        // Only the session status-change event drives the KycProfile; any other event family
        // (data.updated, user.*, business.*, activity.*, transaction.*) is acknowledged, not applied.
        var decision = string.Equals(webhookType, StatusUpdatedType, StringComparison.OrdinalIgnoreCase)
            ? MapDecision(status)
            : KycDecision.Pending;

        return new KycCallbackResult(sessionId, decision, reason, eventId);
    }

    // Didit verification statuses (docs.didit.me/integration/verification-statuses).
    // Only the two terminal outcomes move our state; the rest are non-terminal -> Pending (no-op).
    private static KycDecision MapDecision(string status) =>
        status.Trim().ToLowerInvariant() switch
        {
            "approved" => KycDecision.Approved,
            "declined" => KycDecision.Declined,
            // "In Review" / "In Progress" / "Not Started" / "Abandoned" / "Expired" /
            // "KYC Expired" / "Resubmitted" -> acknowledge without changing state.
            _ => KycDecision.Pending
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
