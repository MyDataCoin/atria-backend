using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Realtors.Dtos;
using Atria.Application.Realtors.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Realtor self-service: the authenticated realtor's own business profile.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/realtor")]
[Authorize]
public sealed class RealtorController : ApiControllerBase
{
    public RealtorController(ISender sender) : base(sender) { }

    /// <summary>Returns the current realtor's business profile from <c>realtor_profiles</c>.</summary>
    /// <remarks>
    /// Requires the <c>Realtor</c> role. Returns the profile linked to the authenticated realtor
    /// (full name, position, wallet address, company name / registration number, office address).
    /// Responds with 404 when no profile row has been set up for the realtor yet.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Realtor")]
    [ProducesResponseType<RealtorProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMyRealtorProfileQuery(), ct));
}
