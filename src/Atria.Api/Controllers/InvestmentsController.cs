using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Investments.Commands;
using Atria.Application.Investments.Dtos;
using Atria.Application.Investments.Queries;
using Atria.Domain.Investments;
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

    /// <summary>What a sum buys here, before anything is reserved.</summary>
    /// <remarks>
    /// Call this while the investor is typing. The offering is placed in whole tokens, so a sum
    /// almost never lands exactly on a token boundary: this returns the whole tokens the sum covers,
    /// what they actually cost (<c>totalAmount</c>), and the part of the entered sum that buys nothing
    /// (<c>changeAmount</c>) — which is what the investor should see before confirming, because
    /// <c>POST /investments</c> charges <c>totalAmount</c> and not what they typed. <c>canPurchase</c>
    /// is false when the count falls under the offering's minimum or over what is left; the minimum
    /// and its price come back either way so the form can say what to enter instead.
    /// Reserves nothing and changes nothing.
    /// </remarks>
    /// <param name="propertyId">The offering to quote against.</param>
    /// <param name="amount">The sum the investor entered.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("quote")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<InvestmentQuoteDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Quote(
        [FromQuery] Guid propertyId, [FromQuery] decimal amount, CancellationToken ct)
        => ToActionResult(await Sender.Send(new QuoteInvestmentQuery(propertyId, amount), ct));

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

    /// <summary>Exercises the 14-day right of withdrawal on the caller's own investment.</summary>
    /// <remarks>
    /// Paragraph 44 of the draft Decree: within 14 calendar days of the agreement the holder may walk
    /// away without giving a reason and without penalty. The shares go back on sale, anything already
    /// minted is queued for burning off their address, the registry stops counting it, and the refund
    /// is recorded at the price paid. Paying it is a separate step. 409 once the window has closed —
    /// after that the purchase is final.
    /// </remarks>
    /// <param name="id">The caller's active investment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <summary>Voids an application that should not stand, returning its shares to the issue. Admin only.</summary>
    /// <remarks>
    /// For a mistaken entry, a duplicate, or data left over from testing — the supported way to remove
    /// such an application, so that nobody has to delete a row by hand. A hand-deleted row keeps the
    /// shares out of the pool for good: the reservation already decremented the issue's supply and
    /// nothing puts it back.
    ///
    /// Works on a reserved or an active application, the latter also outside the 14-day window. An
    /// application that had been activated is unwound like a withdrawal: the register stops counting it,
    /// anything minted is burned, and — unless <c>recordRefund</c> is false — the money owed back is
    /// recorded. Pass <c>recordRefund: false</c> only when nothing was ever received for it. 409 when
    /// the application is already closed.
    /// </remarks>
    /// <param name="id">Id of the application to annul.</param>
    /// <param name="request">The reason, and whether money is owed back.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/annul")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Annul(Guid id, AnnulInvestmentRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new AnnulInvestmentCommand(id, request.Reason, request.RecordRefund), ct));

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new WithdrawInvestmentCommand(id), ct));

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

    /// <summary>Applications across every investor — the operator's queue. Admin only.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role. This is the one investment read that crosses investor boundaries,
    /// which is why it is separate from <c>/investments/me</c>. Filter with <c>status</c>
    /// (<c>Reserved</c>, <c>Active</c>, <c>Rejected</c>, <c>Cancelled</c>, <c>Expired</c>) and
    /// <c>propertyId</c>; newest first, <c>take</c> capped at 500.
    /// </remarks>
    /// <param name="status">Lifecycle status to narrow to; omit for all.</param>
    /// <param name="propertyId">Issue to narrow to; omit for all.</param>
    /// <param name="take">How many to return; capped at 500, defaults to 100.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<IReadOnlyList<InvestmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] InvestmentStatus? status, [FromQuery] Guid? propertyId, [FromQuery] int take,
        CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new ListInvestmentsQuery(status, propertyId, take <= 0 ? 100 : take), ct));

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

    /// <summary>The on-chain record of one investment: address, contract, transaction, settlement.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role. The owner may read their own record and an Admin
    /// may read any; for anyone else the row is reported as not found, since an address and a transaction
    /// hash together identify a person's holding.
    ///
    /// Fields are null rather than invented when there is nothing to report: an investment whose shares
    /// have not been minted has no transaction, and a network with no configured explorer yields addresses
    /// without links.
    /// </remarks>
    /// <param name="id">Id of the investment.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}/chain")]
    [Authorize(Roles = "Investor,Admin")]
    [ProducesResponseType<InvestmentChainRecordDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChainRecord(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetInvestmentChainRecordQuery(id), ct));
}
