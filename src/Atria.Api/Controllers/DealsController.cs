using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Deals.Commands;
using Atria.Application.Deals.Dtos;
using Atria.Application.Deals.Queries;
using Atria.Application.Properties.Dtos;
using Atria.Application.Properties.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Realtor dashboard: referral deals, the investor headline counter, the full property catalogue,
/// plus public resolution of a referral link for the investor landing page.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/deals")]
[Authorize]
public sealed class DealsController : ApiControllerBase
{
    public DealsController(ISender sender) : base(sender) { }

    /// <summary>Creates a referral deal for a property and returns it with its shareable link. Realtor only.</summary>
    /// <remarks>
    /// Requires the <c>Realtor</c> role. Picks one <b>open</b> property, records the realtor's
    /// commission percent (0–100), and generates a unique referral link that lives for 14 days. The
    /// deal starts <c>pending</c>; it becomes <c>successful</c> when an investor buys through the link
    /// and <c>rejected</c> if the link expires unused. Responds with 404 when the property does not
    /// exist or is not open.
    /// </remarks>
    /// <param name="request">The target property and the realtor's commission percent.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Realtor")]
    [ProducesResponseType<DealDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(CreateDealRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreateDealCommand(request.PropertyId, request.CommissionPercent), ct);
        return ToActionResult(result);
    }

    /// <summary>Lists every referral deal owned by the current realtor. Realtor only.</summary>
    /// <remarks>
    /// Requires the <c>Realtor</c> role. Returns the caller's deals with their status, commission,
    /// referral link, and expiry; the list is empty when the realtor has none.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Realtor")]
    [ProducesResponseType<IReadOnlyList<DealDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMyDealsQuery(), ct));

    /// <summary>Headline count of registered investors for the dashboard. Realtor only.</summary>
    /// <remarks>
    /// Requires the <c>Realtor</c> role. Returns only the number of active investor accounts — never
    /// any investor data.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("investor-count")]
    [Authorize(Roles = "Realtor")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InvestorCount(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetInvestorCountQuery(), ct));

    /// <summary>The full property catalogue with complete information, for the realtor home page. Realtor only.</summary>
    /// <remarks>
    /// Requires the <c>Realtor</c> role. Returns every property (all statuses, including drafts) with
    /// full details so the realtor can pick one when creating a deal.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("properties")]
    [Authorize(Roles = "Realtor")]
    [ProducesResponseType<IReadOnlyList<PropertyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Properties(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPropertiesQuery(), ct));

    /// <summary>Resolves a referral token to the property it targets. Public.</summary>
    /// <remarks>
    /// Anonymous endpoint for the investor landing page: given a referral token, returns the target
    /// property id and whether the link is still valid. Never exposes the realtor's commission.
    /// Responds with 404 when the token is unknown.
    /// </remarks>
    /// <param name="token">The referral token from the shared link.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("by-token/{token}")]
    [AllowAnonymous]
    [ProducesResponseType<ReferralResolutionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveReferral(string token, CancellationToken ct)
        => ToActionResult(await Sender.Send(new ResolveReferralQuery(token), ct));
}
