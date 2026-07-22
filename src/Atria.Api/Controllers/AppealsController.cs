using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Appeals.Commands;
using Atria.Application.Appeals.Dtos;
using Atria.Application.Appeals.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Ban appeals: submitted anonymously from the blocked screen, read by the super admin.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/appeals")]
public sealed class AppealsController : ApiControllerBase
{
    public AppealsController(ISender sender) : base(sender) { }

    /// <summary>Submits a ban appeal. Anonymous — a banned user has no token.</summary>
    /// <remarks>
    /// No authentication. A banned admin/realtor sends this from the "you are blocked" screen. The
    /// message is required; the username is stored as-is so the super admin can match it to an account.
    /// </remarks>
    /// <param name="request">The username tried and the appeal message.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The appeal was recorded.</response>
    /// <response code="400">The message is missing.</response>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Submit(SubmitAppealRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitAppealCommand(request.Username, request.Message), ct);
        return ToCreatedResult(result, nameof(GetAll), null);
    }

    /// <summary>Lists ban appeals, newest first. Super admin only.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. Each row carries the appeal id, the username tried, the
    /// appellant's full name (resolved from the username when possible, else null), the message and the
    /// submission time. Empty list when there are none, not a 404.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The appeals list (possibly empty).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    [ProducesResponseType<IReadOnlyList<AppealDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetAppealsQuery(), ct));
}
