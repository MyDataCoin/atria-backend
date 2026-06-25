using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
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
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? entityType,
        [FromQuery] Guid? entityId,
        CancellationToken ct)
    {
        var result = await Sender.Send(new GetAuditLogQuery(entityType, entityId), ct);
        return ToActionResult(result);
    }
}
