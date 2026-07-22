using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Investments.Commands;
using Atria.Application.Investments.Dtos;
using Atria.Application.Investments.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Investments (offering applications): apply, approve/reject/cancel, list, fetch, portfolio totals.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/investments")]
[Authorize]
public sealed class InvestmentsController : ApiControllerBase
{
    public InvestmentsController(ISender sender) : base(sender) { }

    /// <summary>Submits an offering application (Reserved) for the current investor.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. The investor's KYC must be approved and the
    /// target property must exist, be open, and have enough remaining token capacity for the requested
    /// <c>Amount</c>. The requested tokens are reserved from the pool immediately (so the offering cannot be
    /// oversubscribed) and the application's currency/price are taken from the property. There is no payment:
    /// an operator later approves the application to activate it via <c>POST /investments/{id}/approve</c>.
    /// </remarks>
    /// <param name="request">The property to invest in and the amount to commit.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateInvestmentRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new CreateInvestmentCommand(request.PropertyId, request.Amount, request.ReferralToken), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Approves a reserved application, activating the investment. Operator only.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role. Replaces the old payment callback: confirming the (off-platform)
    /// settlement moves the application from Reserved to Active, allowlists the wallet, enqueues the on-chain
    /// token allocation, and settles any referral deal. 409 when the application is not awaiting approval.
    /// </remarks>
    /// <param name="id">Id of the reserved application to approve.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ApproveInvestmentCommand(id), ct));

    /// <summary>Rejects a reserved application, returning its tokens to the pool. Operator only.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role. Moves the application from Reserved to Rejected and returns its reserved
    /// tokens to the property's pool. A <c>reason</c> is required. 409 when the application is not awaiting approval.
    /// </remarks>
    /// <param name="id">Id of the reserved application to reject.</param>
    /// <param name="request">Body carrying the required rejection <c>reason</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, RejectInvestmentRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RejectInvestmentCommand(id, request.Reason), ct));

    /// <summary>Cancels the caller's own reserved application, returning its tokens to the pool.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role. The caller must own the application and it must still be awaiting
    /// approval; the reserved tokens are returned to the property's pool.
    /// </remarks>
    /// <param name="id">Id of the caller's reserved application to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new CancelInvestmentCommand(id), ct));

    /// <summary>Lists every investment owned by the current investor.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. Returns only the investments owned by the
    /// authenticated caller; the list is empty when the investor has none.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<IReadOnlyList<InvestmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyInvestmentsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Aggregated portfolio totals for the current investor.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. Returns the total invested amount, the
    /// count of active investments, and the underlying investment list for the authenticated caller.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("portfolio")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<PortfolioDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Portfolio(CancellationToken ct)
    {
        var result = await Sender.Send(new GetPortfolioQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single investment by id (owner or Admin).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role and a valid bearer token. The owner may read their
    /// own investment and an Admin may read any; for anyone else the row is reported as not found so its
    /// existence is not leaked.
    /// </remarks>
    /// <param name="id">Id of the investment to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Admin")]
    [ProducesResponseType<InvestmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetInvestmentByIdQuery(id), ct);
        return ToActionResult(result);
    }
}
