using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Operations.Commands;
using Atria.Application.Operations.Dtos;
using Atria.Application.Operations.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Body of a report of what an issue earned and spent over one period.</summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="StartUtc">First day of the period, inclusive.</param>
/// <param name="EndUtc">Last day of the period, inclusive.</param>
/// <param name="GrossRevenue">Rent and other income collected. Two decimal places.</param>
/// <param name="OperatingExpenses">Costs incurred. Two decimal places.</param>
/// <param name="Note">What the period covers, in the reporter's words.</param>
/// <param name="Lines">Optional breakdown: <c>[{ "kind": "revenue", "label": "Аренда 1 этаж", "amount": 420000 }]</c>.</param>
public sealed record ReportOperatingPeriodRequest(
    Guid PropertyId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal GrossRevenue,
    decimal OperatingExpenses,
    string? Note,
    IReadOnlyList<OperatingPeriodLineInput>? Lines);

/// <summary>Body of a revision to a draft period.</summary>
/// <param name="GrossRevenue">Corrected income.</param>
/// <param name="OperatingExpenses">Corrected costs.</param>
/// <param name="Note">Note; <c>null</c> leaves the existing one.</param>
/// <param name="Lines">Replaces the breakdown when supplied; <c>null</c> leaves it, <c>[]</c> clears it.</param>
public sealed record ReviseOperatingPeriodRequest(
    decimal GrossRevenue,
    decimal OperatingExpenses,
    string? Note,
    IReadOnlyList<OperatingPeriodLineInput>? Lines);

/// <summary>
/// What an issue earned and spent, period by period — the figures a dividend is declared from.
/// </summary>
/// <remarks>
/// This is the answer to "where did the distributed amount come from?". A dividend must name a
/// confirmed period and cannot exceed its net income, so every payment traces back to income the
/// owner's side signed off on rather than to a number typed into a form.
///
/// Reporting and confirming are deliberately separate acts by different people, which is what the
/// management company asked for. A period is a draft until confirmed: revisable, and unusable for a
/// distribution.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/operating-periods")]
[Authorize(Roles = "Admin,Finance,Auditor")]
public sealed class OperatingPeriodsController : ApiControllerBase
{
    public OperatingPeriodsController(ISender sender) : base(sender) { }

    /// <summary>Reports a period's figures. Admin or Finance.</summary>
    /// <remarks>
    /// Created as a draft that no distribution may be declared from. 409 when a period covering
    /// exactly these dates has already been reported for the issue — the same income must not be
    /// reported, confirmed and distributed twice.
    /// </remarks>
    /// <param name="request">The period and its figures.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin,Finance")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Report(ReportOperatingPeriodRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new ReportOperatingPeriodCommand(
            request.PropertyId, request.StartUtc, request.EndUtc, request.GrossRevenue,
            request.OperatingExpenses, request.Note, request.Lines), ct);

        return ToCreatedResult(
            result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>An issue's periods, most recent first. Admin, Finance or Auditor.</summary>
    /// <param name="propertyId">The issue.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OperatingPeriodDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] Guid propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ListOperatingPeriodsQuery(propertyId), ct));

    /// <summary>One period with its breakdown. Admin, Finance or Auditor.</summary>
    /// <param name="id">The period.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<OperatingPeriodDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetOperatingPeriodQuery(id), ct));

    /// <summary>Revises a draft period's figures. Admin or Finance.</summary>
    /// <remarks>
    /// 409 once the period is confirmed. A distribution may already have been sized against those
    /// figures, and moving the ceiling afterwards is how money goes out that the income never covered.
    /// </remarks>
    /// <param name="id">The period.</param>
    /// <param name="request">The corrected figures.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin,Finance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Revise(
        Guid id, ReviseOperatingPeriodRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ReviseOperatingPeriodCommand(
            id, request.GrossRevenue, request.OperatingExpenses, request.Note, request.Lines), ct));

    /// <summary>Confirms a period's figures. Admin or Finance.</summary>
    /// <remarks>
    /// The owner's sign-off, and the point from which a dividend may be declared. 409 when the caller
    /// is the same person who reported the figures — confirming exists to put a second pair of eyes on
    /// them, so one account cannot do both halves.
    /// </remarks>
    /// <param name="id">The period.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = "Admin,Finance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ConfirmOperatingPeriodCommand(id), ct));

    /// <summary>Downloads an issue's periods as CSV. Admin, Finance or Auditor.</summary>
    /// <remarks>
    /// The "выгрузка из системы" the management company asked for. Rendered server-side and
    /// byte-for-byte reproducible.
    /// </remarks>
    /// <param name="propertyId">The issue.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("export")]
    [Produces("text/csv", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Export([FromQuery] Guid propertyId, CancellationToken ct)
    {
        var result = await Sender.Send(new ExportOperatingPeriodsQuery(propertyId), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var file = result.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
