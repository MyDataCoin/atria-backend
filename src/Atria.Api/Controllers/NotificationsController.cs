using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Notifications.Commands;
using Atria.Application.Notifications.Dtos;
using Atria.Application.Notifications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>The current user's notifications.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/notifications")]
[Authorize]
public sealed class NotificationsController : ApiControllerBase
{
    public NotificationsController(ISender sender) : base(sender) { }

    /// <summary>Lists the current user's notifications.</summary>
    /// <remarks>
    /// Returns every notification addressed to the authenticated caller, newest first,
    /// including its read/unread flag. Requires an authenticated user (any role); the result
    /// is scoped to the caller, so one user can never see another user's notifications.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The caller's notifications.</response>
    /// <response code="401">The request is not authenticated.</response>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<NotificationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyNotificationsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Marks one of the current user's notifications as read.</summary>
    /// <remarks>
    /// Marks the specified notification as read. Requires an authenticated user (any role) and
    /// resource ownership: only the notification's owner may mark it read; another user receives
    /// 403. The operation is idempotent — marking an already-read notification succeeds with no change.
    /// </remarks>
    /// <param name="id">Identifier of the notification to mark as read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The notification was marked as read.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The notification belongs to another user.</response>
    /// <response code="404">No notification exists with the supplied id.</response>
    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new MarkNotificationReadCommand(id), ct);
        return ToActionResult(result);
    }
}
