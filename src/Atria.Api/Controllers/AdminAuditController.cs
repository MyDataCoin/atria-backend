using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Audit.Dtos;
using Atria.Application.Audit.Queries;
using Atria.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Read-only audit trail, filterable by entity. Admin / Compliance only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit")]
[Authorize(Roles = "Admin,Compliance,SuperAdmin")]
public sealed class AdminAuditController : ApiControllerBase
{
    public AdminAuditController(ISender sender) : base(sender) { }

    /// <summary>Queries a page of the audit journal, newest first.</summary>
    /// <remarks>
    /// Returns audit entries ordered by <c>occurredOnUtc</c> descending. Every filter is optional and
    /// they combine (AND): <paramref name="entityType"/> scopes to one aggregate kind,
    /// <paramref name="entityId"/> to one instance, <paramref name="eventType"/> to one action
    /// (e.g. <c>PropertyPublished</c>), and <paramref name="severity"/> to one criticality level
    /// (<c>success</c> | <c>warning</c> | <c>alert</c>). <c>page</c> defaults to 1 and
    /// <c>pageSize</c> to 50 (capped at 200).
    ///
    /// Each entry carries a ready-to-render <c>summary</c> and the actor's display name
    /// (<c>actorName</c>) — note the actor is not always an admin: an investor opening a support
    /// ticket is audited too. Entries are append-only; nothing here can be edited or deleted.
    /// Requires the <c>Admin</c> or <c>Compliance</c> role.
    /// </remarks>
    /// <param name="entityType">Aggregate/entity name (e.g. <c>Property</c>, <c>Publication</c>, <c>SupportTicket</c>).</param>
    /// <param name="entityId">Identifier of a specific entity instance.</param>
    /// <param name="eventType">Action name (e.g. <c>PropertyCreated</c>, <c>TicketClosed</c>).</param>
    /// <param name="severity">Criticality: <c>success</c>, <c>warning</c>, or <c>alert</c>.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (max 200).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">One page of matching audit entries.</response>
    /// <response code="400">The severity filter is not a recognized value.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Query(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        [FromQuery] string? eventType,
        [FromQuery] string? severity,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
    {
        var result = await Sender.Send(
            new GetAuditLogQuery(entityType, entityId, eventType, severity, page, pageSize), ct);
        return ToActionResult(result);
    }
}
