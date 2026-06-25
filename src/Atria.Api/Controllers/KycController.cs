using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Kyc.Commands;
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
    [HttpPost("submit")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Submit(SubmitKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitKycCommand(
            request.Provider, request.WalletAddress, request.FullName,
            request.DocumentNumber, request.Nationality), ct);
        return ToActionResult(result);
    }

    /// <summary>Returns the current investor's KYC profile state.</summary>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetKycStatusQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>A Compliance officer approves or rejects a KYC profile under review.</summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Compliance")]
    public async Task<IActionResult> Review(Guid id, ReviewKycRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new ReviewKycCommand(id, request.Approve, request.Reason), ct);
        return ToActionResult(result);
    }
}
