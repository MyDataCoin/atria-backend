using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Regulatory.Commands;
using Atria.Application.Regulatory.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Notifications owed to the regulator, with the deadlines the draft Decree sets. Each obligation is
/// recorded the moment it arises, its content is generated from platform data, and filing is recorded
/// against it — so a missed deadline is visible before it is missed, not after.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/regulatory-reports")]
[Authorize(Roles = "Admin,Auditor")]
public sealed class RegulatoryReportsController : ApiControllerBase
{
    public RegulatoryReportsController(ISender sender) : base(sender) { }

    /// <summary>Notifications not yet filed, earliest deadline first — the reminder list.</summary>
    /// <remarks>
    /// Every row carries the working days left and whether it is already overdue. Deadlines run in
    /// working days, so a notification due "in 5 days" across a weekend is nearer than it looks.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("outstanding")]
    [ProducesResponseType<IReadOnlyList<RegulatoryReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Outstanding(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetOutstandingReportsQuery(), ct));

    /// <summary>Every notification, newest period first.</summary>
    /// <param name="propertyId">Issue to narrow to; omit for all.</param>
    /// <param name="take">How many to return; capped at 200, defaults to 50.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RegulatoryReportDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? propertyId, [FromQuery] int take, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ListRegulatoryReportsQuery(propertyId, take <= 0 ? 50 : take), ct));

    /// <summary>One notification with its generated content.</summary>
    /// <param name="id">Obligation identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<RegulatoryReportDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetRegulatoryReportQuery(id), ct));

    /// <summary>Records a filing obligation and its deadline. Admin only.</summary>
    /// <remarks>
    /// Periodic obligations (monthly, quarterly) are recorded automatically as each period closes;
    /// this is for the event-driven ones — the §24 notification on funds received, and the §§50/52
    /// results once a sale completes. Idempotent per (kind, issue, period).
    /// </remarks>
    /// <param name="request">Which notification, for which issue and period.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Raise(RaiseRegulatoryReportRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RaiseRegulatoryReportCommand(
            request.Kind, request.PropertyId, request.PeriodStartUtc, request.PeriodEndUtc), ct);
        return ToCreatedResult(result, nameof(Get), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Generates the document from platform data. Admin only.</summary>
    /// <remarks>
    /// Can be re-run until the notification is filed — figures move up to the moment it is handed over.
    /// 409 when the data it needs is missing: the placement results require a reporting snapshot of the
    /// holder register, and the interim collateral report requires a recorded appraisal.
    /// </remarks>
    /// <param name="id">Obligation identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/generate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Generate(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GenerateRegulatoryReportCommand(id), ct));

    /// <summary>Records that the notification was filed, with the regulator's reference. Admin only.</summary>
    /// <remarks>
    /// Filing after the deadline is recorded in the audit journal as an alert rather than silently
    /// accepted. A filed notification can no longer be regenerated.
    /// </remarks>
    /// <param name="id">Obligation identifier.</param>
    /// <param name="request">The regulator's acknowledgement reference.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/file")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> File(Guid id, MarkReportFiledRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new MarkRegulatoryReportFiledCommand(id, request.FilingReference), ct));
}
