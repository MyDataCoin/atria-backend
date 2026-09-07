using System.Globalization;
using System.Text.Json;
using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Trading.Commands;
using Atria.Application.Trading.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// The seam with the licensed trading operator: the trade cards they push in, and the operator-facing
/// views that show how those cards line up with the chain.
/// </summary>
/// <remarks>
/// Only the ingest endpoint is anonymous, and only at the transport layer — the body is authenticated
/// by its signature before a single field is read. Everything else is staff-only.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/exchange")]
[Authorize(Roles = "Admin,CollateralManager,Auditor")]
public sealed class ExchangeController : ApiControllerBase
{
    private const string SignatureHeader = "X-Atria-Signature";
    private const string TimestampHeader = "X-Atria-Timestamp";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IExchangeSignatureVerifier _verifier;

    public ExchangeController(ISender sender, IExchangeSignatureVerifier verifier) : base(sender)
        => _verifier = verifier;

    /// <summary>Receives one settled or cancelled trade from the trading operator.</summary>
    /// <remarks>
    /// Anonymous at the transport layer; authenticity comes from an HMAC-SHA256 signature over
    /// <c>{timestamp}.{body}</c> in <c>X-Atria-Signature</c>, with the timestamp in
    /// <c>X-Atria-Timestamp</c>. An unsigned or stale event is refused outright — reporting is built
    /// on these events, so one that cannot be authenticated is worse than one that never arrived.
    ///
    /// Processing is exactly-once by <c>event_id</c>: a redelivery is acknowledged without recording a
    /// second trade, which is what makes it safe for the operator to retry for 24 hours. The status
    /// codes are chosen for that retry policy — 400 means the event is malformed and repeating it
    /// will not help, while 409 and 5xx mean try again.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The event was recorded, or recognised as one already recorded.</response>
    /// <response code="400">The body is not a usable trade event. Do not retry unchanged.</response>
    /// <response code="401">Signature missing, invalid or stale; the body is not trusted.</response>
    /// <response code="404">The event names a token contract we do not run.</response>
    /// <response code="409">Temporarily unprocessable, e.g. a reversal that arrived before its trade. Retry.</response>
    [HttpPost("trades")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType<ExchangeTradeReceipt>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReceiveTrade(CancellationToken ct)
    {
        // Program enables buffering on this route so the body can be read raw. The signature covers
        // exact bytes, so it must be verified before any parsing normalizes them.
        Request.EnableBuffering();
        Request.Body.Position = 0;
        using var reader = new StreamReader(Request.Body, leaveOpen: true);
        var rawBody = await reader.ReadToEndAsync(ct);
        Request.Body.Position = 0;

        var signature = Header(SignatureHeader);
        var timestamp = Header(TimestampHeader);

        var verdict = _verifier.Verify(rawBody, signature, timestamp);
        if (verdict != SignatureVerdict.Valid)
        {
            // The reason is logged by the verifier but not returned. Telling an unauthenticated
            // caller whether they got the key wrong or merely the clock is free reconnaissance.
            return Unauthorized();
        }

        ExchangeTradeEventDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExchangeTradeEventDto>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ToActionResult(Result.Failure(Error.Validation(
                "trade.malformedJson", $"The event body is not valid JSON: {ex.Message}")));
        }

        if (dto is null)
        {
            return ToActionResult(Result.Failure(Error.Validation(
                "trade.emptyBody", "The event body is empty.")));
        }

        var result = await Sender.Send(new RecordExchangeTradeCommand(dto), ct);
        return ToActionResult(result);
    }

    /// <summary>Trades reported for an issue, newest settlement first.</summary>
    /// <param name="propertyId">The issue.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("trades")]
    [ProducesResponseType<IReadOnlyList<ExchangeTradeDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTrades([FromQuery] Guid propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetExchangeTradesQuery(propertyId), ct));

    /// <summary>Compares the operator's trade reports against the chain for one issue.</summary>
    /// <remarks>
    /// The daily check. Four findings are possible: a card whose settlement never appeared on chain,
    /// a card that describes a transaction differently than the chain does, a gap in the operator's
    /// event numbering, and — the important one — a holder-to-holder transfer on chain that no card
    /// explains. That last case is a trade we were not told about, which means it is valued at
    /// nominal price and its travel-rule disclosure is incomplete.
    ///
    /// 502 when the node cannot be reached, because "we could not ask" must never be reported as
    /// "everything reconciles".
    /// </remarks>
    /// <param name="propertyId">The issue to check.</param>
    /// <param name="fromUtc">Start of the period; defaults to 24 hours before <paramref name="toUtc"/>.</param>
    /// <param name="toUtc">End of the period; defaults to now.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("reconciliation")]
    [ProducesResponseType<TradeReconciliationReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetReconciliation(
        [FromQuery] Guid propertyId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
        => ToActionResult(await Sender.Send(new ReconcileTradesQuery(propertyId, fromUtc, toUtc), ct));

    private string? Header(string name)
        => Request.Headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : null;
}
