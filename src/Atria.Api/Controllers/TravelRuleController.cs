using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.TravelRule;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Body identifying the receiving side of a transfer.</summary>
/// <param name="BeneficiaryName">Who is receiving, as the counterparty verified them.</param>
/// <param name="CounterpartyVasp">The counterparty service provider, if now known.</param>
public sealed record SetTravelRuleBeneficiaryRequest(string BeneficiaryName, string? CounterpartyVasp);

/// <summary>
/// Originator/beneficiary information owed on transfers of shares between service providers
/// (FATF Recommendation 16, the "travel rule").
/// </summary>
/// <remarks>
/// A disclosure is assembled the moment a qualifying transfer is observed on chain, whether or not
/// there is anywhere to send it: a transfer that happened without its information being assembled is
/// a breach that cannot be repaired afterwards, whereas a queued message is work somebody can do.
///
/// Delivery is a separate step. No counterparty service provider is configured yet — which one runs
/// secondary trading is an open decision — so the delivery endpoint reports that plainly instead of
/// marking messages sent.
///
/// These records hold personal data by design. The list views deliberately do not include it.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/travel-rule")]
[Authorize(Roles = "Admin,Auditor")]
public sealed class TravelRuleController : ApiControllerBase
{
    public TravelRuleController(ISender sender) : base(sender) { }

    /// <summary>Disclosures still owed, oldest first — the compliance queue.</summary>
    /// <remarks>Covers both those never delivered and those whose delivery failed.</remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("outstanding")]
    [ProducesResponseType<IReadOnlyList<TravelRuleMessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Outstanding(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetOutstandingTravelRuleMessagesQuery(), ct));

    /// <summary>Disclosures recorded for one issue, newest first.</summary>
    /// <param name="propertyId">The issue.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TravelRuleMessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ByProperty([FromQuery] Guid propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetTravelRuleMessagesByPropertyQuery(propertyId), ct));

    /// <summary>Identifies the receiving side of a transfer. Admin only.</summary>
    /// <remarks>
    /// An outgoing disclosure starts without a beneficiary — we report the address we can see and do
    /// not guess at the person behind it. This is how that gap is closed once the counterparty
    /// answers.
    /// </remarks>
    /// <param name="id">The disclosure.</param>
    /// <param name="request">Who is receiving, and through whom.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/beneficiary")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetBeneficiary(
        Guid id, [FromBody] SetTravelRuleBeneficiaryRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new SetTravelRuleBeneficiaryCommand(id, request.BeneficiaryName, request.CounterpartyVasp),
            ct));

    /// <summary>Attempts delivery of everything still owed. Admin only.</summary>
    /// <remarks>
    /// Returns the number delivered. While no counterparty service provider is configured this is a
    /// conflict rather than a run that quietly does nothing — the queue is not stuck by accident.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("deliver")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deliver(CancellationToken ct)
        => ToActionResult(await Sender.Send(new DeliverOutstandingTravelRuleMessagesCommand(), ct));
}
