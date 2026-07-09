using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;
using Atria.Domain.Users;

namespace Atria.Application.Support.Queries;

/// <summary>
/// Returns the ticket and its ordered thread. The owner may read their own; an Admin may read any
/// (and gets the <c>investor</c> block). Anyone else sees a not-found so existence is not leaked.
/// </summary>
public sealed class GetTicketByIdQueryHandler
    : IRequestHandler<GetTicketByIdQuery, Result<TicketDto>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly ICurrentUserService _currentUser;

    public GetTicketByIdQueryHandler(ISupportTicketRepository tickets, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _currentUser = currentUser;
    }

    public async Task<Result<TicketDto>> Handle(GetTicketByIdQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<TicketDto>(Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        var isAdmin = _currentUser.IsInRole(Role.Admin);

        var ticket = await _tickets.GetByIdWithMessagesAsync(request.Id, ct);
        if (ticket is null || (!isAdmin && ticket.InvestorId != userId.Value))
            return Result.Failure<TicketDto>(Error.NotFound("ticket.not_found", "Ticket not found."));

        TicketInvestorDto? investor = null;
        if (isAdmin)
        {
            var names = await _tickets.GetInvestorNamesAsync(new[] { ticket.InvestorId }, ct);
            investor = new TicketInvestorDto(ticket.InvestorId, names.GetValueOrDefault(ticket.InvestorId));
        }

        return Result.Success(TicketDto.From(ticket, investor, includeMessages: true));
    }
}
