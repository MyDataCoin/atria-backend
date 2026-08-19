using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.SuperAdmin.Commands;
using Atria.Application.Users.Dtos;
using Atria.Application.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Staff (admin) accounts for the super-admin panel. SuperAdmin only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/admins")]
[Authorize(Roles = "SuperAdmin")]
public sealed class AdminsController : ApiControllerBase
{
    public AdminsController(ISender sender) : base(sender) { }

    /// <summary>Lists the staff (Admin / SuperAdmin) accounts a super admin can manage.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. Returns one row per credential-login staff account —
    /// its <c>users.id</c> (the target for <c>/users/{id}/ban</c> and <c>/password/*</c>), username
    /// and blocked flag. FullName/Email are not stored for staff accounts and come back <c>null</c>.
    /// Empty list when there are none, not a 404.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The staff accounts list (possibly empty).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdminDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAdmins(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetAdminsQuery(), ct));

    /// <summary>Creates a staff (admin) account with a one-time password.</summary>
    /// <remarks>
    /// Requires the <c>SuperAdmin</c> role. The account is created with the username, full name and
    /// password given here and is flagged to change that password on first sign-in — the super admin
    /// hands the password over once, and it stops working as soon as the admin replaces it via
    /// <c>POST /auth/password/change</c>. The password must already satisfy the strength policy
    /// (six or more characters with upper and lower case, a digit and a special character).
    /// <c>409</c> when the username is taken.
    /// </remarks>
    /// <param name="request">Username, full name and the one-time password.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The account was created.</response>
    /// <response code="400">The request failed validation (missing fields or a weak password).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not a super admin.</response>
    /// <response code="409">The username is already taken.</response>
    [HttpPost]
    [ProducesResponseType<AdminDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAdmin(RegisterAdminRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new RegisterAdminCommand(request.Username, request.FullName, request.Password), ct));
}
