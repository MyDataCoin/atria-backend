using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
using Atria.Application.Kyc.Dtos;
using Atria.Application.Kyc.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>KYC submission, the investor's own status, and Compliance review.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/kyc")]
[Authorize]
public sealed class KycController : ApiControllerBase
{
    public KycController(ISender sender) : base(sender) { }

    /// <summary>The investor submits KYC and opens a provider verification session.</summary>
    /// <remarks>
    /// Requires an authenticated user in the <c>Investor</c> role. Ensures a KYC profile exists
    /// for the caller (one per user), opens a verification session with the chosen provider, and
    /// moves the profile to <c>UnderReview</c>. A supplied wallet address must be a valid
    /// 0x-prefixed 40-hex-character address. Resubmitting a profile that is already under review,
    /// approved, or rejected is rejected as a domain rule violation (<c>400</c>).
    /// </remarks>
    /// <param name="request">The provider plus optional wallet address and identity details.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">KYC submitted; the current profile status is returned.</response>
    /// <response code="400">Validation failed, the provider is not configured, or the profile cannot be (re)submitted.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    [HttpPost("submit")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<KycStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Submit(SubmitKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitKycCommand(
            request.Provider, request.WalletAddress, request.FullName,
            request.DocumentNumber, request.Nationality), ct);
        return ToActionResult(result);
    }

    /// <summary>Returns the current investor's KYC profile state.</summary>
    /// <remarks>
    /// Requires an authenticated user in the <c>Investor</c> role. Reads only the caller's own
    /// KYC profile. Returns <c>404</c> when the caller has never submitted KYC.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The caller's KYC profile status.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not an Investor.</response>
    /// <response code="404">The caller has no KYC profile yet.</response>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<KycStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetKycStatusQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>A Compliance officer approves or rejects a KYC profile under review.</summary>
    /// <remarks>
    /// Requires an authenticated user in the <c>Compliance</c> role. Approves or rejects the
    /// target profile; a rejection (<c>Approve=false</c>) must include a <c>Reason</c>. Only a
    /// profile in <c>UnderReview</c> can be decided — deciding any other state is a domain rule
    /// violation (<c>400</c>). Returns <c>204 No Content</c> on success.
    /// </remarks>
    /// <param name="id">The KYC profile identifier to review.</param>
    /// <param name="request">The decision: approve, or reject with a reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="204">The decision was applied.</response>
    /// <response code="400">A rejection reason is missing, or the profile is not in a reviewable state.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="403">The caller is authenticated but not a Compliance officer.</response>
    /// <response code="404">No KYC profile exists with the given id.</response>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Compliance")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Review(Guid id, ReviewKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new ReviewKycCommand(id, request.Approve, request.Reason), ct);
        return ToActionResult(result);
    }
}
