using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;
using Atria.Domain.Support;
using Atria.Domain.Users;

namespace Atria.Application.Support.Commands;

/// <summary>Appends a message to a ticket. The author is derived from the caller's role.</summary>
public sealed record AddTicketMessageCommand(Guid TicketId, string Body)
    : IRequest<Result<TicketMessageDto>>;

/// <summary>
/// Owner (investor) or Admin may reply. An investor reply moves the ticket to Open, an Admin
/// reply (recorded as <c>support</c>) moves it to Pending. Replying to a closed ticket is a 409.
/// </summary>
public sealed class AddTicketMessageCommandHandler
    : IRequestHandler<AddTicketMessageCommand, Result<TicketMessageDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public AddTicketMessageCommandHandler(
        ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<TicketMessageDto>> Handle(AddTicketMessageCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<TicketMessageDto>(Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        var isAdmin = _currentUser.IsInRole(Role.Admin);

        var ticket = await _tickets.GetByIdAsync(request.TicketId, ct);
        // Not found and not-owned are reported identically so ownership is not leaked.
        if (ticket is null || (!isAdmin && ticket.InvestorId != userId.Value))
            return Result.Failure<TicketMessageDto>(Error.NotFound("ticket.not_found", "Ticket not found."));

        if (ticket.Status == TicketStatus.Closed)
            return Result.Failure<TicketMessageDto>(
                Error.Conflict("ticket.closed", "Cannot reply to a closed ticket; reopen it first."));

        var author = isAdmin ? MessageAuthor.Support : MessageAuthor.Investor;
        var message = ticket.AddMessage(author, request.Body);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(TicketMessageDto.From(message));
    }
}
