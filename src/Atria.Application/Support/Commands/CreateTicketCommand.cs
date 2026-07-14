using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;
using Atria.Domain.Support;
using Role = Atria.Domain.Users.Role;

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
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CreateTicketCommandHandler(
        ISupportTicketRepository tickets,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<TicketDto>> Handle(CreateTicketCommand request, CancellationToken ct)
    {
        var investorId = _currentUser.UserId;
        if (investorId is null)
            return Result.Failure<TicketDto>(Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        // Capture the opener's role from the JWT so the admin desk can tell a realtor ticket from an
        // investor one without depending on a users row. Anything non-realtor is treated as investor.
        var authorRole = _currentUser.Role == Role.Realtor ? Role.Realtor : Role.Investor;

        var ticket = SupportTicket.Open(
            investorId.Value, request.Subject, request.Category, request.Body, authorRole);

        await _tickets.AddAsync(ticket, ct);

        // The actor here is the investor/realtor who opened it — not staff. Warning severity: an
        // inbound ticket is something the desk must act on.
        await _audit.WriteAsync(
            Application.Audit.AuditEntities.SupportTicket, ticket.Id,
            Application.Audit.AuditEvents.TicketOpened,
            $"Создан тикет «{ticket.Subject}» ({ticket.Category})",
            Domain.Audit.AuditSeverity.Warning, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        // The investor sees their own ticket: no investor block, thread included.
        return Result.Success(TicketDto.From(ticket, investor: null, includeMessages: true));
    }
}
