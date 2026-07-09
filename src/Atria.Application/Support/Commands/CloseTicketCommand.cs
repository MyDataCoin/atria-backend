using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Support;
using Atria.Domain.Users;

namespace Atria.Application.Support.Commands;

/// <summary>Closes a ticket. Owner (investor) or Admin.</summary>
public sealed record CloseTicketCommand(Guid TicketId) : IRequest<Result>;

/// <summary>
/// Owner or Admin may close the ticket. Closing an already-closed ticket is a 409 (rather than a
/// silent no-op) so the client can distinguish the states.
/// </summary>
public sealed class CloseTicketCommandHandler : IRequestHandler<CloseTicketCommand, Result>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CloseTicketCommandHandler(
        ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CloseTicketCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        var isAdmin = _currentUser.IsInRole(Role.Admin);

        var ticket = await _tickets.GetByIdAsync(request.TicketId, ct);
        if (ticket is null || (!isAdmin && ticket.InvestorId != userId.Value))
            return Result.Failure(Error.NotFound("ticket.not_found", "Ticket not found."));

        if (ticket.Status == TicketStatus.Closed)
            return Result.Failure(Error.Conflict("ticket.already_closed", "Ticket is already closed."));

        ticket.Close();
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
