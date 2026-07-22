using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Realtors.Dtos;
using Atria.Application.Realtors.Queries;
using Atria.Application.SuperAdmin.Commands;
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

    /// <summary>Registers a new realtor account (users row + profile). Super admin only.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. Creates a credential realtor: a <c>users</c> row (role
    /// Realtor, username + hashed password) and its <c>realtor_profiles</c> row. The password is set
    /// by the super admin and stored hashed. On success returns the new realtor as a leaderboard row
    /// (zero deals, not blocked) so the client can add it to the list immediately.
    /// </remarks>
    /// <param name="request">Username, password, full name, and optional company / phone.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The realtor was created.</response>
    /// <response code="400">Required fields are missing (username / password / full name).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="409">The username is already taken.</response>
    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType<RealtorStatsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRealtorRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RegisterRealtorCommand(
            request.Username, request.Password, request.FullName, request.CompanyName, request.PhoneNumber), ct);
        return ToCreatedResult(result, nameof(GetStats), null);
    }
}
