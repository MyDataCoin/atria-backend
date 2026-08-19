using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Feedback.Commands;
using Atria.Application.Feedback.Dtos;
using Atria.Application.Feedback.Queries;
using Atria.Domain.Feedback;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Questions from the feedback form on the public site: submitted anonymously, read by staff.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/feedback")]
public sealed class FeedbackController : ApiControllerBase
{
    public FeedbackController(ISender sender) : base(sender) { }

    /// <summary>Submits a question from the public site. Anonymous.</summary>
    /// <remarks>
    /// No authentication — the sender is a site visitor, not an account. Name, email, phone and the
    /// question are all required. Nothing is emailed anywhere: the question lands in the super-admin
    /// help desk, and the row is deleted automatically after <c>90</c> days, which is what the consent
    /// text on the form promises. The endpoint is rate-limited per IP.
    /// </remarks>
    /// <param name="request">Name, email, phone and the question.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="201">The question was recorded.</response>
    /// <response code="400">A field is missing or too long, or the email/phone is malformed.</response>
    /// <response code="429">Too many submissions from this address; try again later.</response>
    [HttpPost]
    [AllowAnonymous]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit(SubmitFeedbackRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new SubmitFeedbackCommand(request.FullName, request.Email, request.Phone, request.Message), ct);
        return ToCreatedResult(result, nameof(GetAll), null);
    }

    /// <summary>Lists feedback requests, newest first. Admin and super admin.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>SuperAdmin</c> role. Each row carries the sender's name, email
    /// and phone, the question, when it arrived and when it was marked answered (null while open).
    /// Rows older than 90 days are gone — they are swept, not archived.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The requests list (possibly empty).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is neither an admin nor a super admin.</response>
    [HttpGet]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType<IReadOnlyList<FeedbackRequestDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetFeedbackRequestsQuery(), ct));

    /// <summary>Marks a feedback request answered. Admin and super admin.</summary>
    /// <remarks>Idempotent — the first timestamp is kept. <c>404</c> when there is no such request.</remarks>
    /// <param name="id">The request id.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The request is marked answered.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is neither an admin nor a super admin.</response>
    /// <response code="404">No request with that id (it may have passed retention).</response>
    [HttpPost("{id:guid}/handled")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkHandled(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new MarkFeedbackHandledCommand(id), ct));
}
