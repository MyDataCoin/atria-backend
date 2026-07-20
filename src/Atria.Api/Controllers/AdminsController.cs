using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
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
}
