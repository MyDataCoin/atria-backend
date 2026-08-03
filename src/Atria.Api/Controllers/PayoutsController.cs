using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Payouts.Commands;
using Atria.Application.Payouts.Dtos;
using Atria.Application.Payouts.Queries;
using Atria.Domain.Payouts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Body of a request to draw up a distribution.</summary>
/// <param name="SnapshotId">The payout snapshot the distribution is divided across.</param>
/// <param name="Kind">Income distribution (<c>Dividend</c>) or return of capital (<c>CapitalReturn</c>).</param>
/// <param name="Method">How the money will reach holders: <c>BankTransfer</c> or <c>Wallet</c>.</param>
/// <param name="DeclaredAmount">The total being distributed.</param>
/// <param name="Currency">Currency of the declared amount.</param>
/// <param name="Note">What this distribution is, for the audit trail.</param>
public sealed record CreatePayoutRunRequest(
    Guid SnapshotId, PayoutKind Kind, PayoutMethod Method, decimal DeclaredAmount, string Currency,
    string? Note);

/// <summary>Body of a settlement record.</summary>
/// <param name="SettlementReference">Bank payment order number, or the transaction hash.</param>
public sealed record SettlePayoutItemRequest(string SettlementReference);

/// <summary>Body carrying a reason.</summary>
/// <param name="Reason">Why the payment failed, or why the distribution is being called off.</param>
public sealed record PayoutReasonRequest(string Reason);

/// <summary>
/// Distributions to the holders of an issue: dividends and returns of capital.
/// </summary>
/// <remarks>
/// The platform does not move money. What it does is compute what each holder is owed against a
/// frozen register, hold that behind a second approval, and record the reference each payment came
/// back with — so the question "was this holder paid, and on what evidence" has an answer years
/// later.
///
/// A distribution is drawn up here as a draft that authorises nothing. It only opens for settlement
/// after a second person approves it through <c>/governance</c> with kind <c>PayoutRun</c> and the
/// run's id as the target.
/// </remarks>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payouts")]
public sealed class PayoutsController : ApiControllerBase
{
    public PayoutsController(ISender sender) : base(sender) { }

    /// <summary>Draws up a distribution against a frozen holder register. Admin only.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role. The snapshot must have been taken for the <c>Payout</c> purpose
    /// and every holding address in it must resolve to a known investor; otherwise the request is
    /// refused rather than paying an address nobody can name.
    ///
    /// One distribution per cut. A second request against the same snapshot is a conflict, not a
    /// second run — that would divide the same register twice and pay every holder double.
    ///
    /// The result is a draft. Nothing may be settled against it until it has a second approval.
    /// </remarks>
    /// <param name="request">The snapshot, the amount, and how it will be paid.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePayoutRunRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new CreatePayoutRunCommand(
                request.SnapshotId, request.Kind, request.Method, request.DeclaredAmount,
                request.Currency, request.Note),
            ct));

    /// <summary>Distributions on one issue, newest cut first. Admin or Auditor.</summary>
    /// <param name="propertyId">The issue.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [Authorize(Roles = "Admin,Auditor")]
    [ProducesResponseType<IReadOnlyList<PayoutRunDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] Guid propertyId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPayoutRunsQuery(propertyId), ct));

    /// <summary>One distribution with every holder line. Admin or Auditor.</summary>
    /// <remarks>Lines come largest amount first — the order the money is worked through.</remarks>
    /// <param name="id">The distribution.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Admin,Auditor")]
    [ProducesResponseType<PayoutRunDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPayoutRunQuery(id), ct));

    /// <summary>Records that one holder was paid. Admin only.</summary>
    /// <remarks>
    /// Only on an approved distribution, and only once per holder. The reference is what proves the
    /// payment afterwards, so it is required.
    /// </remarks>
    /// <param name="id">The distribution.</param>
    /// <param name="itemId">The holder's line.</param>
    /// <param name="request">The payment reference.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/items/{itemId:guid}/settle")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Settle(
        Guid id, Guid itemId, [FromBody] SettlePayoutItemRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new SettlePayoutItemCommand(id, itemId, request.SettlementReference), ct));

    /// <summary>Records that one holder's payment could not be delivered. Admin only.</summary>
    /// <remarks>
    /// The line keeps its amount and stays owed: a payment that bounced is still a debt, and dropping
    /// it would quietly shrink what the issue distributed.
    /// </remarks>
    /// <param name="id">The distribution.</param>
    /// <param name="itemId">The holder's line.</param>
    /// <param name="request">What went wrong.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/items/{itemId:guid}/fail")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Fail(
        Guid id, Guid itemId, [FromBody] PayoutReasonRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new FailPayoutItemCommand(id, itemId, request.Reason), ct));

    /// <summary>Closes a distribution. Admin only.</summary>
    /// <remarks>
    /// Refused while any holder line is still pending: a pending line means nobody has looked at it,
    /// and closing over it would leave a holder silently unpaid inside a distribution marked done.
    /// </remarks>
    /// <param name="id">The distribution.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new CompletePayoutRunCommand(id), ct));

    /// <summary>Calls off a distribution. Admin only.</summary>
    /// <remarks>Refused once any holder has been paid — that money cannot be recalled from here.</remarks>
    /// <param name="id">The distribution.</param>
    /// <param name="request">Why it is being called off.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid id, [FromBody] PayoutReasonRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new CancelPayoutRunCommand(id, request.Reason), ct));

    /// <summary>The current investor's own payout history.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role. Scoped to the caller — a payout line names an amount, an
    /// address and a person. Draft and cancelled distributions are not shown: telling a holder they
    /// are owed money nobody has authorised is a promise the platform has not made.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<IReadOnlyList<MyPayoutDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMyPayoutsQuery(), ct));
}
