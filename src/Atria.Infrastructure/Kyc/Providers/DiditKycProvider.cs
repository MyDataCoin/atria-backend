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
        // Fail fast on missing config: without a workflow_id Didit returns an opaque 400, and
        // without an api key a 403 — surface a clear, diagnosable reason instead.
        if (string.IsNullOrWhiteSpace(_options.WorkflowId))
            throw new KycProviderException(null, "Didit WorkflowId is not configured (Didit:WorkflowId).");
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new KycProviderException(null, "Didit ApiKey is not configured (Didit:ApiKey).");

        // Didit hosted-session body: workflow_id + a vendor reference we can correlate back to
        // our KycProfile. Optional fields are OMITTED when empty (a null callback or blank
        // contact email would otherwise make Didit 400 the request).
        var body = new Dictionary<string, object?>
        {
            ["workflow_id"] = _options.WorkflowId,
            ["vendor_data"] = request.KycProfileId.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(request.RedirectUrl))
            body["callback"] = request.RedirectUrl;
        // No contact_details: this is a phone-first product with no email addresses. Didit only
        // uses contact_details to email the user the hosted-flow link, which we don't need — we
        // return the url to the client directly. workflow_id + vendor_data is all Didit requires.

        using var message = new HttpRequestMessage(HttpMethod.Post, SessionEndpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json")
        };
        // Didit authenticates session creation with the API key.
        message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            // Transport failure (DNS/TLS/connection) — a downstream problem, not a server bug.
            _logger.LogError(ex, "Didit session creation transport failure.");
            throw new KycProviderException(null, "Didit is currently unreachable.", ex);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                // Log the provider's response body (truncated) so a 403 (bad/revoked x-api-key) or
                // 400 (invalid workflow_id/body) is diagnosable from OUR logs — never returned to the client.
                _logger.LogError(
                    "Didit session creation failed. Status={Status} Body={Body}",
                    (int)response.StatusCode, Truncate(json, 1000));
                throw new KycProviderException(
                    (int)response.StatusCode,
                    $"Didit rejected the session request (status={(int)response.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // NOTE: Didit returns "session_id" and the hosted "url" to redirect the user to.
            var sessionId = GetString(root, "session_id", "id")
                ?? throw new KycProviderException(null, "Didit response missing session id.");
            var verificationUrl = GetString(root, "url", "verification_url", "session_url")
                ?? throw new KycProviderException(null, "Didit response missing verification url.");

            return new KycSessionResult(sessionId, verificationUrl);
        }
    }

    // Bounds a provider response body before logging so a large/hostile payload can't bloat logs.
    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max] + "…";

    public async Task<KycVerifiedIdentity?> RetrieveVerifiedIdentityAsync(string sessionId, CancellationToken ct)
    {
        // Best-effort enrichment: any missing config / transport / status failure returns null so
        // the caller falls back to the self-reported name — never blocks the approval side effects.
        if (string.IsNullOrWhiteSpace(sessionId))
            return null;
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("Didit ApiKey not configured; cannot retrieve verified identity.");
            return null;
        }

        // Retrieve Session Decision: GET /v3/session/{session_id}/decision/ (x-api-key).
        using var message = new HttpRequestMessage(
            HttpMethod.Get, $"/v3/session/{Uri.EscapeDataString(sessionId)}/decision/");
        message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Didit decision retrieval transport failure for session {SessionId}.", sessionId);
            return null;
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Didit decision retrieval failed. Status={Status} Body={Body}",
                    (int)response.StatusCode, Truncate(json, 1000));
                return null;
            }

            return ParseVerifiedIdentity(json);
        }
    }

    // Extracts the verified name from a Didit decision payload. In v3 user-KYC the verified ID
    // fields live in the id_verifications[] feature array; other/older shapes nest them under an
    // id_verification / kyc / decision object. We probe those locations in order and take the
    // first that carries a name. Any parse failure yields null (enrichment is best-effort).
    private KycVerifiedIdentity? ParseVerifiedIdentity(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            foreach (var source in EnumerateIdentitySources(root))
            {
                var first = GetString(source, "first_name", "firstName", "given_name");
                var last = GetString(source, "last_name", "lastName", "family_name", "surname");
                var full = GetString(source, "full_name", "fullName", "name");

                if (!string.IsNullOrWhiteSpace(first) ||
                    !string.IsNullOrWhiteSpace(last) ||
                    !string.IsNullOrWhiteSpace(full))
                    return new KycVerifiedIdentity(first, last, full);
            }

            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Didit decision payload.");
            return null;
        }
    }

    // Candidate JSON objects that may carry the verified name, in priority order.
    private static IEnumerable<JsonElement> EnumerateIdentitySources(JsonElement root)
    {
        // v3 user KYC: id_verifications is an array of feature results.
        if (root.TryGetProperty("id_verifications", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object)
                    yield return item;

        // Alternative shapes: a single object under one of these keys.
        foreach (var key in IdentityObjectKeys)
            if (root.TryGetProperty(key, out var obj) && obj.ValueKind == JsonValueKind.Object)
                yield return obj;

        // Last resort: the fields sit directly on the root.
        yield return root;
    }

    private static readonly string[] IdentityObjectKeys = { "id_verification", "kyc", "decision" };

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
