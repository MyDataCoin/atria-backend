using System.Globalization;
using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Commands;
using Atria.Application.Kyc.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Inbound provider webhooks (KYC + payments). Anonymous at the transport layer —
/// authenticity is verified inside the matching Strategy (signature + replay + idempotency).
/// The controller only assembles the raw <see cref="WebhookPayload"/> and dispatches it.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/webhooks")]
[AllowAnonymous]
public sealed class WebhooksController : ApiControllerBase
{
    // Common header names providers use to carry the HMAC/asymmetric signature.
    private static readonly string[] SignatureHeaderNames =
    [
        "X-Signature", "X-Hub-Signature-256", "X-Hub-Signature",
        "Stripe-Signature", "X-Didit-Signature", "X-Webhook-Signature"
    ];

    // Common header names providers use to carry the event timestamp (for replay protection).
    private static readonly string[] TimestampHeaderNames =
    [
        "X-Timestamp", "X-Request-Timestamp", "X-Webhook-Timestamp"
    ];

    public WebhooksController(ISender sender) : base(sender) { }

    /// <summary>KYC provider callback. The decision only moves <c>KycProfile</c> State.</summary>
    [HttpPost("kyc/{provider}")]
    [Consumes("application/json", "text/plain")]
    public async Task<IActionResult> Kyc(string provider, CancellationToken ct)
    {
        var payload = await BuildPayloadAsync(ct);
        var result = await Sender.Send(new HandleKycCallbackCommand(provider, payload), ct);
        return WebhookResult(result);
    }

    /// <summary>Payment provider callback. The decision only moves <c>Investment</c> State (idempotent).</summary>
    [HttpPost("payments/{provider}")]
    [Consumes("application/json", "text/plain")]
    public async Task<IActionResult> Payments(string provider, CancellationToken ct)
    {
        var payload = await BuildPayloadAsync(ct);
        var result = await Sender.Send(new HandlePaymentCallbackCommand(provider, payload), ct);
        return WebhookResult(result);
    }

    /// <summary>Reads the RAW request body and headers into a <see cref="WebhookPayload"/>.</summary>
    private async Task<WebhookPayload> BuildPayloadAsync(CancellationToken ct)
    {
        // Program enables request buffering for the webhook routes so the body can be read raw here.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var headers = Request.Headers.ToDictionary(
            h => h.Key,
            h => h.Value.ToString(),
            StringComparer.OrdinalIgnoreCase);

        var signature = FirstHeader(headers, SignatureHeaderNames);
        var timestamp = ParseTimestamp(FirstHeader(headers, TimestampHeaderNames));
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        return new WebhookPayload(rawBody, headers, signature, timestamp, sourceIp);
    }

    /// <summary>
    /// Webhooks always ack quickly: success (verified or duplicate) -&gt; 204; signature
    /// failure -&gt; 401; any other failure -&gt; the mapped status (so the provider can retry).
    /// </summary>
    private IActionResult WebhookResult(Result result)
    {
        if (result.IsSuccess)
            return NoContent();

        return result.Error.Type == ErrorType.Unauthorized
            ? Unauthorized()
            : ToActionResult(result);
    }

    private static string? FirstHeader(IReadOnlyDictionary<string, string> headers, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static DateTimeOffset? ParseTimestamp(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Unix seconds (common for HMAC schemes) first, then ISO-8601.
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);

        return DateTimeOffset.TryParse(
            raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
