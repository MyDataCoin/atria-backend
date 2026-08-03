using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Governance.Commands;
using Atria.Application.Governance.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Dual approval for actions no single person may take alone: starting a payout, blocking an investor,
/// publishing an issue. One person raises the request, a different person decides it, and only then is
/// the action carried out. The record of who asked and who approved survives the decision.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/governance/critical-actions")]
[Authorize(Roles = "Admin,SuperAdmin")]
public sealed class GovernanceController : ApiControllerBase
{
    public GovernanceController(ISender sender) : base(sender) { }

    /// <summary>Raises a request for a second approval.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>SuperAdmin</c> role. One request per (kind, target) at a time:
    /// asking again while one is open returns the open request rather than creating a competing one.
    /// Some flows raise the request for you — <c>POST /properties/{id}/publish</c> is one.
    /// </remarks>
    /// <param name="request">What is being asked for, on which target, and why.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Request(RequestCriticalActionRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new RequestCriticalActionCommand(request.Kind, request.TargetId, request.Reason), ct);
        return ToCreatedResult(result, nameof(GetPending), null);
    }

    /// <summary>Requests awaiting a decision, oldest first.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("pending")]
    [ProducesResponseType<IReadOnlyList<CriticalActionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPending(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPendingCriticalActionsQuery(), ct));

    /// <summary>Recently decided requests, newest first — who approved what, and who refused.</summary>
    /// <param name="take">How many to return; capped at 200.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("decided")]
    [ProducesResponseType<IReadOnlyList<CriticalActionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDecided([FromQuery] int take, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetDecidedCriticalActionsQuery(take <= 0 ? 50 : take), ct));

    /// <summary>Approves someone else's request and carries the action out.</summary>
    /// <remarks>
    /// Refused with 409 when the caller is the person who raised the request, when it has already been
    /// decided, or when its approval window has closed. The action runs in the same transaction as the
    /// approval: if it fails, nothing is recorded as approved.
    /// </remarks>
    /// <param name="id">The pending request.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ApproveCriticalActionCommand(id), ct));

    /// <summary>Declines someone else's request. A reason is required.</summary>
    /// <param name="id">The pending request.</param>
    /// <param name="request">Why it is being declined.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, RejectCriticalActionRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RejectCriticalActionCommand(id, request.Note), ct));

    /// <summary>Withdraws one's own request before anyone has decided it.</summary>
    /// <param name="id">The pending request.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new WithdrawCriticalActionCommand(id), ct));
}
