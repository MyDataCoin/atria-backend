using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Investments.Commands;
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
    [HttpPost("{applicationId:guid}/payments")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> CreatePayment(
        Guid applicationId, CreatePaymentRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreatePaymentSessionCommand(applicationId, request.Provider), ct);
        return ToActionResult(result);
    }

    /// <summary>Lists every investment owned by the current investor.</summary>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyInvestmentsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Aggregated portfolio totals for the current investor.</summary>
    [HttpGet("portfolio")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Portfolio(CancellationToken ct)
    {
        var result = await Sender.Send(new GetPortfolioQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single investment by id (owner or Admin).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Admin")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetInvestmentByIdQuery(id), ct);
        return ToActionResult(result);
    }
}
