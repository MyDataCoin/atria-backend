using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Applications.Commands;
using Atria.Application.Applications.Dtos;
using Atria.Application.Applications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Investor applications to invest in a property, plus Compliance approve/reject.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications")]
[Authorize]
public sealed class ApplicationsController : ApiControllerBase
{
    public ApplicationsController(ISender sender) : base(sender) { }

    /// <summary>Creates a draft application for the current investor.</summary>
    /// <remarks>
    /// Opens a new investment application in <c>Draft</c> status for the authenticated investor and
    /// returns its id. Requires the <b>Investor</b> role and an <b>approved KYC</b> profile; the target
    /// property must exist and be active, and the requested amount must not exceed the property's
    /// remaining token capacity. Submit it afterwards via <c>POST /applications/{id}/submit</c> to send it for review.
    /// </remarks>
    /// <param name="request">Target property id and the amount the investor wishes to commit.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateApplicationRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreateApplicationCommand(request.PropertyId, request.Amount), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Lists the current investor's applications.</summary>
    /// <remarks>
    /// Returns every application owned by the authenticated investor across all statuses. Requires the
    /// <b>Investor</b> role; the result is always scoped to the caller and never exposes other investors' applications.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<IReadOnlyList<ApplicationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyApplicationsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single application (owner, Compliance, or Admin).</summary>
    /// <remarks>
    /// Returns one application by id. Access is granted to the application's <b>owner</b> or to callers
    /// holding the <b>Compliance</b> or <b>Admin</b> role; any other authenticated investor receives 403.
    /// Requires one of the <b>Investor</b>, <b>Compliance</b>, or <b>Admin</b> roles.
    /// </remarks>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Compliance,Admin")]
    [ProducesResponseType<ApplicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetApplicationByIdQuery(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Submits the investor's own draft application for review.</summary>
    /// <remarks>
    /// Transitions the application from <c>Draft</c> to <c>Submitted</c> (State pattern), queuing it for
    /// Compliance review. Requires the <b>Investor</b> role and that the caller <b>owns</b> the application;
    /// submitting from a non-draft status fails with 409. Returns 204 No Content on success.
    /// </remarks>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitApplicationCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Approves an application. Compliance only.</summary>
    /// <remarks>
    /// Transitions a submitted/under-review application to <c>Approved</c> (State pattern), clearing the way
    /// for investment. Requires the <b>Compliance</b> role; approving from an invalid status fails with 409.
    /// Returns 204 No Content on success.
    /// </remarks>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Compliance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ApproveApplicationCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Rejects an application with a reason. Compliance only.</summary>
    /// <remarks>
    /// Transitions a submitted/under-review application to <c>Rejected</c> (State pattern) and records the
    /// supplied reason on the application. Requires the <b>Compliance</b> role; a non-empty reason is
    /// mandatory and rejecting from an invalid status fails with 409. Returns 204 No Content on success.
    /// </remarks>
    /// <param name="id">The application's unique identifier.</param>
    /// <param name="request">The rejection reason shown to the investor.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Compliance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, RejectApplicationRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RejectApplicationCommand(id, request.Reason), ct);
        return ToActionResult(result);
    }
}
