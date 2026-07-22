using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.SuperAdmin.Commands;
using Atria.Application.SuperAdmin.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Super-admin account operations: ban/unban any user and reset/restore admin &amp; realtor
/// passwords. Restricted to the <c>SuperAdmin</c> role — sensitive actions over other people's
/// accounts, all journalled to the audit log.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize(Roles = "SuperAdmin")]
public sealed class SuperAdminController : ApiControllerBase
{
    public SuperAdminController(ISender sender) : base(sender) { }

    /// <summary>Bans a user account (investor or realtor) so it can no longer authenticate.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. <paramref name="id"/> is the <c>users.id</c>. Idempotent.
    /// A banned account is refused a token at login (403 <c>auth.account_banned</c>). An optional
    /// <c>reason</c> in the body is stored, journalled, and returned to the banned user as
    /// <c>banReason</c> on that 403. Reflected as <c>blocked: true</c> in <c>GET /users</c> and
    /// <c>GET /realtors/stats</c>.
    /// </remarks>
    /// <param name="id">The user id to ban.</param>
    /// <param name="request">Optional body carrying a ban <c>reason</c> shown to the user; may be omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The account was banned.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="404">No user with that id.</response>
    [HttpPost("{id:guid}/ban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Ban(Guid id, [FromBody] BanUserRequest? request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new BanUserCommand(id, request?.Reason), ct));

    /// <summary>Lifts a ban on a user account.</summary>
    /// <remarks>Requires the <c>SuperAdmin</c> role. <paramref name="id"/> is the <c>users.id</c>. Idempotent.</remarks>
    /// <param name="id">The user id to unban.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The ban was lifted.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="404">No user with that id.</response>
    [HttpPost("{id:guid}/unban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unban(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new UnbanUserCommand(id), ct));

    /// <summary>Resets an admin's or realtor's password and returns the new temporary password.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. <paramref name="id"/> is the <c>users.id</c>. The body is
    /// optional — omit it to have the server generate a temporary password, or supply
    /// <c>newPassword</c> to set an explicit one. The account is flagged to change it on next use.
    /// Investors sign in by phone OTP and have no password, so a reset on one returns <c>409</c>.
    /// </remarks>
    /// <param name="id">The user id whose password to reset.</param>
    /// <param name="request">Optional explicit new password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The password was reset; the temporary password is returned.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="404">No user with that id.</response>
    /// <response code="409">The target account has no password (an investor).</response>
    [HttpPost("{id:guid}/password/reset")]
    [ProducesResponseType<ResetPasswordResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetPassword(
        Guid id, [FromBody] ResetPasswordRequest? request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ResetUserPasswordCommand(id, request?.NewPassword), ct));

    /// <summary>Restores an admin's or realtor's access by clearing the forced-reset flag.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. <paramref name="id"/> is the <c>users.id</c>. A separate,
    /// separately-audited action from the reset. Returns <c>409</c> for an investor or when no reset
    /// is pending.
    /// </remarks>
    /// <param name="id">The user id whose access to restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">Access was restored.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="404">No user with that id.</response>
    /// <response code="409">The target has no password, or no reset is pending.</response>
    [HttpPost("{id:guid}/password/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RestorePassword(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RestoreUserPasswordCommand(id), ct));
}
