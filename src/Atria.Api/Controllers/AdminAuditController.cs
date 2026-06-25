using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Audit.Dtos;
using Atria.Application.Audit.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Read-only audit trail, filterable by entity. Admin / Compliance only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/audit")]
[Authorize(Roles = "Admin,Compliance")]
public sealed class AdminAuditController : ApiControllerBase
{
    public AdminAuditController(ISender sender) : base(sender) { }

    /// <summary>Queries the audit log, optionally filtered by entity type and/or id.</summary>
    /// <remarks>
    /// Returns audit trail entries, newest first. Both filters are optional and combine (AND):
    /// supply <paramref name="entityType"/> to scope to one aggregate kind and/or
    /// <paramref name="entityId"/> to scope to one instance; omit both to read the full trail.
    /// Requires the <c>Admin</c> or <c>Compliance</c> role.
    /// </remarks>
    /// <param name="entityType">Optional aggregate/entity name to filter by (e.g. <c>Investment</c>, <c>KycProfile</c>).</param>
    /// <param name="entityId">Optional identifier of a specific entity instance to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The matching audit log entries.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AuditLogDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Query(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        CancellationToken ct)
    {
        var result = await Sender.Send(new GetAuditLogQuery(entityType, entityId), ct);
        return ToActionResult(result);
    }
}
