using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Kyc;

namespace Atria.Infrastructure.Kyc.Providers;

/// <summary>
/// Manual / internal KYC provider. There is no external vendor: the "session" is a
/// no-op pointing at an internal review URL, and the "callback" is an internal,
/// already-trusted decision payload produced by back-office review. Selected through
/// DI by <see cref="ProviderType"/> (Strategy).
/// </summary>
public sealed class ManualKycProvider : IKycProviderStrategy
{
    private const string InternalReviewPath = "/internal/kyc/manual-review/";

    public KycProviderType ProviderType => KycProviderType.Manual;

    public Task<KycSessionResult> CreateSessionAsync(KycSessionRequest request, CancellationToken ct)
    {
        // No external hosted flow: the session id is the profile id and the URL is internal.
        var sessionId = request.KycProfileId.ToString("N");
        var url = $"{InternalReviewPath}{sessionId}";
        return Task.FromResult(new KycSessionResult(sessionId, url));
    }

    // NEVER trusted over the wire. A manual decision has an authenticated path of its own —
    // POST /kyc/{id}/review under [Authorize(Roles = "Compliance")] — so nothing legitimate
    // reaches this provider through the anonymous webhook endpoint. Returning true here let
    // anyone POST /webhooks/kyc/manual with their own (non-secret) session id and approve
    // their own KYC. The webhook dispatcher also refuses this provider outright; this is the
    // second lock on the same door.
    public bool VerifySignature(WebhookPayload payload) => false;

    public KycCallbackResult ParseCallback(WebhookPayload payload)
    {
        using var doc = JsonDocument.Parse(payload.RawBody);
        var root = doc.RootElement;

        var sessionId = GetString(root, "sessionId", "kycProfileId")
            ?? throw new InvalidOperationException("Manual KYC callback missing session id.");
        var approved = root.TryGetProperty("approved", out var a) &&
                       a.ValueKind == JsonValueKind.True;
        var reason = GetString(root, "reason");
        var decision = approved ? KycDecision.Approved : KycDecision.Declined;

        // Idempotency key must be STABLE across redeliveries. If the back office did not
        // supply an explicit eventId, derive a deterministic one from session+decision so
        // the same manual decision is never processed twice (a fresh Guid would defeat dedup).
        var eventId = GetString(root, "eventId") ?? $"manual:{sessionId}:{decision}";

        return new KycCallbackResult(sessionId, decision, reason, eventId);
    }

    // No external vendor to query: manual review has no verified-identity source, so the name
    // recorded at submit stands. Returns null so callers fall back to the self-reported name.
    public Task<KycVerifiedIdentity?> RetrieveVerifiedIdentityAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<KycVerifiedIdentity?>(null);

    // Manual review has no provider to ask: the decision is made by a person, here, through
    // ReviewKycCommand. Null says "no such API", which is not the same as "no decision".
    public Task<KycProviderDecision?> RetrieveDecisionAsync(string sessionId, CancellationToken ct)
        => Task.FromResult<KycProviderDecision?>(null);

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
