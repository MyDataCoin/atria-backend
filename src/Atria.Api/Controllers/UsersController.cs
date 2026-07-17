using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Investments.Dtos;
using Atria.Application.Investments.Queries;
using Atria.Application.Users.Dtos;
using Atria.Application.Users.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Users overview (users joined with their KYC). Admin / Compliance only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/users")]
[Authorize(Roles = "Admin,Compliance,SuperAdmin")]
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

    /// <summary>Lists an investor's active holdings, one row per property. Admin / Compliance only.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>Compliance</c> role. Returns each active holding of the investor
    /// <paramref name="id"/>: the property id and (denormalized) name, tokens held, invested amount and its
    /// currency (returned as stored, not converted), the share of the property (<c>tokens / totalTokens *
    /// 100</c>, computed server-side), and the status (always <c>Active</c>). Pending/unpaid investments are
    /// excluded. An investor with no active holdings yields an empty list, not a 404.
    /// </remarks>
    /// <param name="id">Id of the investor whose portfolio to read.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The investor's active holdings (possibly empty).</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet("{id:guid}/investments")]
    [ProducesResponseType<IReadOnlyList<UserInvestmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvestments(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetUserInvestmentsQuery(id), ct);
        return ToActionResult(result);
    }
}
