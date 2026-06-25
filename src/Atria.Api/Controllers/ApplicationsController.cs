using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Applications.Commands;
using Atria.Application.Applications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Investor applications to invest in a property, plus Compliance approve/reject.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/applications")]
[Authorize]
public sealed class ApplicationsController : ApiControllerBase
{
    public ApplicationsController(ISender sender) : base(sender) { }

    /// <summary>Creates a draft application for the current investor.</summary>
    [HttpPost]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Create(CreateApplicationRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreateApplicationCommand(request.PropertyId, request.Amount), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Lists the current investor's applications.</summary>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyApplicationsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single application (owner, Compliance, or Admin).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Compliance,Admin")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetApplicationByIdQuery(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Submits the investor's own draft application for review.</summary>
    [HttpPost("{id:guid}/submit")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Submit(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new SubmitApplicationCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Approves an application. Compliance only.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Compliance")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ApproveApplicationCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Rejects an application with a reason. Compliance only.</summary>
    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Compliance")]
    public async Task<IActionResult> Reject(Guid id, RejectApplicationRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new RejectApplicationCommand(id, request.Reason), ct);
        return ToActionResult(result);
    }
}
