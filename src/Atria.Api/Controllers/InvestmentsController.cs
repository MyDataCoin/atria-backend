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

/// <summary>Investments: start a payment session, list, fetch, and portfolio totals.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/investments")]
[Authorize]
public sealed class InvestmentsController : ApiControllerBase
{
    public InvestmentsController(ISender sender) : base(sender) { }

    /// <summary>Starts a hosted payment session for the investment of an approved application.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. The investment is resolved from the
    /// approved <paramref name="applicationId"/>; the caller must own that investment, it must still be
    /// awaiting payment, and the investor's KYC must be approved at payment time. The
    /// <c>Provider</c> in the body selects the payment strategy and is sent by name
    /// (for example <c>Stripe</c> or <c>BankTransfer</c>). On success a session id and an optional hosted
    /// payment URL are returned for the client to complete the purchase. No money is moved here; activation
    /// happens later via the provider callback.
    /// </remarks>
    /// <param name="applicationId">Id of the approved application whose investment is being paid for.</param>
    /// <param name="request">Payment session request carrying the desired provider (sent by name).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{applicationId:guid}/payments")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<PaymentSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreatePayment(
        Guid applicationId, CreatePaymentRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreatePaymentSessionCommand(applicationId, request.Provider), ct);
        return ToActionResult(result);
    }

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
