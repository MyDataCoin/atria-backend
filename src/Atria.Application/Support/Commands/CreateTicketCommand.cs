using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;
using Atria.Domain.Support;

namespace Atria.Application.Support.Commands;

/// <summary>Opens a new support ticket for the current investor, seeded with a first message.</summary>
public sealed record CreateTicketCommand(string Subject, string Category, string Body)
    : IRequest<Result<TicketDto>>;

/// <summary>
/// Creates the ticket (Open) with the investor's opening message and returns the full ticket
/// (including that message). The investor is taken from the JWT, never the body.
/// </summary>
public sealed class CreateTicketCommandHandler
    : IRequestHandler<CreateTicketCommand, Result<TicketDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CreateTicketCommandHandler(
        ISupportTicketRepository tickets, IUnitOfWork unitOfWork, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<TicketDto>> Handle(CreateTicketCommand request, CancellationToken ct)
    {
        var investorId = _currentUser.UserId;
        if (investorId is null)
            return Result.Failure<TicketDto>(Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        var ticket = SupportTicket.Open(investorId.Value, request.Subject, request.Category, request.Body);

        await _tickets.AddAsync(ticket, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // The investor sees their own ticket: no investor block, thread included.
        return Result.Success(TicketDto.From(ticket, investor: null, includeMessages: true));
    }
}
