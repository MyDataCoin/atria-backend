using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Support.Commands;
using Atria.Application.Support.Dtos;
using Atria.Application.Support.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Support / help-desk tickets, shared by the investor and realtor dashboards and the admin panel.
/// Scope is decided server-side from the JWT role: an Investor or Realtor sees and touches only
/// their own tickets, an Admin sees all and replies as <c>support</c>. Message author is never
/// trusted from the body.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/support/tickets")]
[Authorize]
public sealed class SupportController : ApiControllerBase
{
    public SupportController(ISender sender) : base(sender) { }

    /// <summary>Lists tickets, scoped by role (Investor: own; Admin: all).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role. An Investor receives only their own
    /// tickets; an Admin receives all tickets, each carrying an <c>investor</c> block. The optional
    /// <c>status</c> filter (<c>open</c> | <c>pending</c> | <c>closed</c>) is an admin convenience.
    /// Message threads are omitted from this list; fetch a ticket by id for the full thread.
    /// </remarks>
    /// <param name="status">Optional status filter: open, pending, or closed.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [Authorize(Roles = "Investor,Realtor,Admin")]
    [ProducesResponseType<IReadOnlyList<TicketDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetTickets([FromQuery] string? status, CancellationToken ct)
    {
        var result = await Sender.Send(new GetTicketsQuery(status), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single ticket with its full message thread (owner or Admin).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role. The owner may read their own ticket and an
    /// Admin may read any; for anyone else the ticket is reported as not found so its existence is
    /// not leaked. The response includes the ordered message thread (oldest first).
    /// </remarks>
    /// <param name="id">Id of the ticket to fetch.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Realtor,Admin")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetTicketByIdQuery(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Opens a new ticket for the current investor or realtor.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Realtor</c> role. The ticket is created <c>open</c> and
    /// seeded with the supplied body as the first client message. On success the full ticket
    /// (including that message) is returned.
    /// </remarks>
    /// <param name="request">Subject, category, and opening message.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Investor,Realtor")]
    [ProducesResponseType<TicketDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new CreateTicketCommand(request.Subject, request.Category, request.Body), ct);
        return ToCreatedResult(result, nameof(GetById),
            new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    /// <summary>Appends a message to a ticket (owner or Admin).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role and access to the ticket. The author is
    /// derived from the role — an investor reply is <c>investor</c> and moves the ticket to
    /// <c>open</c>; an Admin reply is <c>support</c> and moves it to <c>pending</c>. Replying to a
    /// closed ticket is rejected with 409. On success the created message is returned.
    /// </remarks>
    /// <param name="id">Id of the ticket to reply to.</param>
    /// <param name="request">The reply body.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/messages")]
    [Authorize(Roles = "Investor,Realtor,Admin")]
    [ProducesResponseType<TicketMessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMessage(Guid id, AddTicketMessageRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new AddTicketMessageCommand(id, request.Body), ct);
        return ToCreatedResult(result, nameof(GetById), new { id });
    }

    /// <summary>Closes a ticket (owner or Admin).</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> or <c>Admin</c> role and access to the ticket. Sets the status to
    /// <c>closed</c>. Closing an already-closed ticket is rejected with 409.
    /// </remarks>
    /// <param name="id">Id of the ticket to close.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Investor,Realtor,Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Close(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new CloseTicketCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Reopens a closed ticket (Admin only).</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> role. Moves a <c>closed</c> ticket back to <c>open</c> for the admin
    /// panel. Reopening a ticket that is not closed is rejected with 409.
    /// </remarks>
    /// <param name="id">Id of the ticket to reopen.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ReopenTicketCommand(id), ct);
        return ToActionResult(result);
    }
}
