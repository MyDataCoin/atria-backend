using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Realtors.Dtos;
using Atria.Application.Realtors.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Realtor reporting for the admin dashboard (deal statistics). Admin / Compliance only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/realtors")]
[Authorize(Roles = "Admin,Compliance,SuperAdmin")]
public sealed class RealtorsController : ApiControllerBase
{
    public RealtorsController(ISender sender) : base(sender) { }

    /// <summary>Realtor leaderboard: closed and total deal counts per realtor, ranked by closed deals.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>Compliance</c> role. Returns one row per realtor — id, full name,
    /// company name, number of <c>Successful</c> (closed) deals, and total deals (pending + successful +
    /// rejected) — ordered by closed deals descending. The client derives the tier from the closed count.
    /// Realtors with no deals appear with zero counts; when there are no realtors the list is empty, not a 404.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The realtor statistics list (possibly empty).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet("stats")]
    [ProducesResponseType<IReadOnlyList<RealtorStatsDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStats(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetRealtorStatsQuery(), ct));
}
