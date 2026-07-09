using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Support;

namespace Atria.Application.Support.Commands;

/// <summary>Reopens a closed ticket (Closed -> Open). Admin only (admin-panel extra).</summary>
public sealed record ReopenTicketCommand(Guid TicketId) : IRequest<Result>;

/// <summary>
/// Reopens a closed ticket for the admin panel. Reopening a ticket that is not closed is a 409.
/// Role is enforced at the controller (Admin), so no per-row ownership check is needed here.
/// </summary>
public sealed class ReopenTicketCommandHandler : IRequestHandler<ReopenTicketCommand, Result>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;

    public ReopenTicketCommandHandler(ISupportTicketRepository tickets, IUnitOfWork unitOfWork)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ReopenTicketCommand request, CancellationToken ct)
    {
        var ticket = await _tickets.GetByIdAsync(request.TicketId, ct);
        if (ticket is null)
            return Result.Failure(Error.NotFound("ticket.not_found", "Ticket not found."));

        if (ticket.Status != TicketStatus.Closed)
            return Result.Failure(Error.Conflict("ticket.not_closed", "Only a closed ticket can be reopened."));

        ticket.Reopen();
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
