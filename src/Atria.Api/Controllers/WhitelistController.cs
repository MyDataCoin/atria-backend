using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Whitelist.Commands;
using Atria.Application.Whitelist.Dtos;
using Atria.Application.Whitelist.Queries;
using Atria.Domain.Whitelist;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// The whitelist queue and the mint lists built from it: every purchase request on its way to being
/// minted, and the batches handed to the exchange to mint them.
/// </summary>
/// <remarks>
/// Operator-facing throughout — the queue carries every investor's request, so it never reaches the
/// investor surface. Not to be confused with the on-chain allowlist, which the Compliance module keeps
/// on <c>Allowlist.sol</c>; this is the working queue that decides what gets minted and when.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/whitelist")]
// Reads are open to the auditor as well: what was minted, for whom, and on whose signature is exactly
// what that role exists to check. Assembling and moving batches stays with the operator.
[Authorize(Roles = "Admin,Auditor")]
public sealed class WhitelistController : ApiControllerBase
{
    public WhitelistController(ISender sender) : base(sender) { }

    /// <summary>The whitelist queue — purchase requests and where each stands.</summary>
    /// <remarks>
    /// A request enters the queue the moment the investor submits a purchase, becomes mintable when an
    /// operator approves the application, and leaves it either into a mint list or excluded.
    /// </remarks>
    /// <param name="propertyId">Narrow to one issuance; omit for the queue across all of them.</param>
    /// <param name="status">Narrow to one status; omit for every status.</param>
    /// <param name="take">How many rows to return; capped at 500.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WhitelistEntryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetWhitelist(
        [FromQuery] Guid? propertyId, [FromQuery] WhitelistStatus? status,
        [FromQuery] int take, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new GetWhitelistQuery(propertyId, status, take <= 0 ? 200 : take), ct));

    /// <summary>Assembles a mint list from the issuance's mintable requests. Admin only.</summary>
    /// <remarks>
    /// The batch and the requests it claims move in one transaction, so a request can never appear in
    /// two batches. 404 when the issuance or a named request does not exist; 409 when there is nothing
    /// mintable, or a named request is not approved, already batched, or has no wallet address.
    /// </remarks>
    /// <param name="request">Issuance, requests to include, and a note.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("mint-lists")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateMintList(CreateMintListRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new CreateMintListCommand(request.PropertyId, request.EntryIds ?? [], request.Note), ct);

        return ToCreatedResult(result, nameof(GetMintList), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Mint lists, newest first. Headers only.</summary>
    /// <param name="propertyId">Narrow to one issuance; omit for all of them.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("mint-lists")]
    [ProducesResponseType<IReadOnlyList<MintListDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetMintLists([FromQuery] Guid? propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMintListsQuery(propertyId), ct));

    /// <summary>A mint list with all of its lines — the JSON form of what goes to the exchange.</summary>
    /// <param name="id">Batch identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("mint-lists/{id:guid}")]
    [ProducesResponseType<MintListDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMintList(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMintListQuery(id), ct));

    /// <summary>Downloads a mint list as CSV — the file handed to the exchange.</summary>
    /// <remarks>
    /// Rendered server-side and byte-for-byte reproducible: exporting the same batch twice yields the
    /// same file, which is what lets both sides compare what they hold.
    /// </remarks>
    /// <param name="id">Batch identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("mint-lists/{id:guid}/export")]
    [Produces("text/csv", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportMintList(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ExportMintListQuery(id), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var file = result.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Records that the batch was handed to the exchange. Admin only.</summary>
    /// <remarks>
    /// Fixes the batch: from here it is a document someone else is acting on. 409 when it is not a draft.
    /// </remarks>
    /// <param name="id">Batch identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("mint-lists/{id:guid}/send")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkSent(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new MarkMintListSentCommand(id), ct));

    /// <summary>Records that the exchange minted the batch. Admin only.</summary>
    /// <remarks>
    /// Carries every request in the batch to minted in the same transaction. 409 when the batch was
    /// never sent — a batch the exchange did not receive cannot have been executed.
    /// </remarks>
    /// <param name="id">Batch identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("mint-lists/{id:guid}/execute")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkExecuted(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new MarkMintListExecutedCommand(id), ct));

    /// <summary>Calls a batch off and returns its requests to the mintable pool. Admin only.</summary>
    /// <remarks>
    /// Allowed on a sent batch too — an exchange can decline one — but never on an executed batch:
    /// those shares exist. 409 in that case.
    /// </remarks>
    /// <param name="id">Batch identifier.</param>
    /// <param name="request">The reason it is being called off; required.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("mint-lists/{id:guid}/cancel")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancelMintListRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new CancelMintListCommand(id, request.Reason), ct));
}
