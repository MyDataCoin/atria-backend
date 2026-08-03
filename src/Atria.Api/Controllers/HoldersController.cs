using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Holders.Commands;
using Atria.Application.Holders.Dtos;
using Atria.Application.Holders.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Holder register: who holds what in an issuance now, and the frozen snapshots that record who held
/// what at a given cut. Operator-facing throughout — the register carries every holder of an issuance,
/// so it is never exposed to investors.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/holders")]
// Reads are open to the collateral manager and the auditor as well: the register is exactly the part
// of the platform their roles exist to watch. Taking a snapshot stays with the operator.
[Authorize(Roles = "Admin,CollateralManager,Auditor")]
public sealed class HoldersController : ApiControllerBase
{
    public HoldersController(ISender sender) : base(sender) { }

    /// <summary>Current holder register of an issuance, largest holding first.</summary>
    /// <remarks>
    /// Open to <c>Admin</c>, <c>CollateralManager</c> and <c>Auditor</c>. <c>search</c> matches an address
    /// fragment or a whole investor id.
    /// Zero-balance and non-allowlisted positions are included: an address can lose its allowlist standing
    /// yet keep its shares, and such holders belong in the register, flagged.
    /// </remarks>
    /// <param name="propertyId">The issuance whose register to read.</param>
    /// <param name="search">Address fragment or investor id; omit for the full register.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HolderPositionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRegistry(
        [FromQuery] Guid propertyId, [FromQuery] string? search, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetHolderRegistryQuery(propertyId, search), ct));

    /// <summary>Freezes the holder register of an issuance at a cut.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role — reading the register is wider than cutting it. Idempotent by
    /// (property, cut, purpose): repeating the request for
    /// the same cut returns the snapshot already taken rather than building a second one, so trades that
    /// settled in between never change it. Omitting <c>snapshotAtUtc</c> cuts at the current instant; a
    /// future instant is rejected.
    /// </remarks>
    /// <param name="request">Issuance, cut and purpose of the snapshot.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("snapshots")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSnapshot(CreateHolderSnapshotRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new CreateHolderSnapshotCommand(request.PropertyId, request.SnapshotAtUtc, request.Purpose), ct);
        return ToCreatedResult(result, nameof(GetSnapshot), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Snapshots taken for an issuance, newest cut first. Headers only.</summary>
    /// <param name="propertyId">The issuance whose snapshots to list.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("snapshots")]
    [ProducesResponseType<IReadOnlyList<HolderSnapshotDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSnapshots([FromQuery] Guid propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetHolderSnapshotsQuery(propertyId), ct));

    /// <summary>A frozen snapshot with all of its rows.</summary>
    /// <param name="id">Snapshot identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("snapshots/{id:guid}")]
    [ProducesResponseType<HolderSnapshotDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSnapshot(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetHolderSnapshotQuery(id), ct));

    /// <summary>Downloads a snapshot as CSV.</summary>
    /// <remarks>
    /// The file is rendered server-side and is byte-for-byte reproducible:
    /// what the operator hands to a regulator is exactly what the register holds.
    /// </remarks>
    /// <param name="id">Snapshot identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("snapshots/{id:guid}/export")]
    [Produces("text/csv", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportSnapshot(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ExportHolderSnapshotQuery(id), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var file = result.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
