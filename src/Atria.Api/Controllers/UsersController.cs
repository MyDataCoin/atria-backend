using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Users.Dtos;
using Atria.Application.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Users overview (users joined with their KYC). Admin / Compliance only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize(Roles = "Admin,Compliance")]
public sealed class UsersController : ApiControllerBase
{
    public UsersController(ISender sender) : base(sender) { }

    /// <summary>Lists all users with their (optional) KYC profile, newest first.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>Compliance</c> role. Returns each user's id, phone, KYC
    /// full name (decrypted server-side), wallet address, KYC status and account creation time.
    /// KYC fields are <c>null</c> for users without a profile.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The users overview list.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<UserOverviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await Sender.Send(new GetUsersOverviewQuery(), ct);
        return ToActionResult(result);
    }
}
