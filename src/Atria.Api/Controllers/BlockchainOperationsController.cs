using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Compliance.Commands;
using Atria.Application.Compliance.Dtos;
using Atria.Application.Compliance.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>The on-chain operations queue: what the platform is doing on chain, and what stalled.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/blockchain-operations")]
[Authorize(Roles = "Admin")]
public sealed class BlockchainOperationsController : ApiControllerBase
{
    public BlockchainOperationsController(ISender sender) : base(sender) { }

    /// <summary>Lists on-chain operations, newest first. Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. There is no manual mint in the platform: shares appear only as
    /// a consequence of an approved application (application → reservation → approval → allowlist →
    /// mint), so that every issued share traces back to a specific investor and a specific ground.
    /// The cost of that design is that everything after approval happens without an operator
    /// touching it — this endpoint is the window onto it.
    ///
    /// Each row carries the status, the confirmation depth observed so far, the transaction hash with
    /// a ready-made <c>explorerUrl</c>, and the verbatim failure text when it failed. Filter with
    /// <c>status</c> (<c>created</c> | <c>submitted</c> | <c>confirmed</c> | <c>failed</c>);
    /// <c>failed</c> is what an operator checking on a stalled run wants.
    /// </remarks>
    /// <param name="status">Optional status filter.</param>
    /// <param name="limit">Rows to return, newest first. Defaults to 50, capped at 200.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BlockchainOperationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? status, [FromQuery] int limit = 50, CancellationToken ct = default)
        => ToActionResult(await Sender.Send(new ListBlockchainOperationsQuery(status, limit), ct));

    /// <summary>Re-queues a failed operation. Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. Operations fail for reasons fixed outside the platform — a
    /// minter wallet out of gas, an unreachable node — and once the cause is dealt with the work
    /// still has to be redone. The operation keeps its idempotency key and its attempt count, so a
    /// retry cannot become a second mint and a repeatedly failing operation cannot hide behind a
    /// clean-looking row. Responds 409 unless the operation is <c>failed</c>: a submitted one may
    /// still be in flight, and re-queueing it would race the worker's own reconciliation.
    /// </remarks>
    /// <param name="id">The failed operation.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RetryBlockchainOperationCommand(id), ct));
}
