using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Support.Dtos;
using Atria.Domain.Support;
using Atria.Domain.Users;

namespace Atria.Application.Support.Queries;

/// <summary>
/// Returns tickets scoped by role. For Admin callers each ticket carries an <c>investor</c> block
/// (id + decrypted KYC name), resolved in one batched lookup to avoid N+1.
/// </summary>
public sealed class GetTicketsQueryHandler
    : IRequestHandler<GetTicketsQuery, Result<IReadOnlyList<TicketDto>>>
{
    private readonly ISupportTicketRepository _tickets;
    private readonly ICurrentUserService _currentUser;

    public GetTicketsQueryHandler(ISupportTicketRepository tickets, ICurrentUserService currentUser)
    {
        _tickets = tickets;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<TicketDto>>> Handle(GetTicketsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<IReadOnlyList<TicketDto>>(
                Error.Unauthorized("ticket.unauthorized", "Authentication is required."));

        if (!TryParseStatus(request.Status, out var status))
            return Result.Failure<IReadOnlyList<TicketDto>>(
                Error.Validation("ticket.invalid_status", "Status must be one of: open, pending, closed."));

        var isAdmin = _currentUser.IsInRole(Role.Admin);

        var tickets = isAdmin
            ? await _tickets.GetAllAsync(status, ct)
            : await _tickets.GetByInvestorAsync(userId.Value, status, ct);

        // Only Admin views expose who opened the ticket; resolve all names in one query. The role
        // comes from the ticket itself (captured at creation), not from a users lookup.
        IReadOnlyDictionary<Guid, string?> names = isAdmin
            ? await _tickets.GetAuthorNamesAsync(
                tickets.Select(t => t.InvestorId).Distinct().ToList(), ct)
            : new Dictionary<Guid, string?>();

        IReadOnlyList<TicketDto> dtos = tickets
            .Select(t => TicketDto.From(t, investor: InvestorFor(isAdmin, t, names)))
            .ToList();

        return Result.Success(dtos);
    }

    private static TicketInvestorDto? InvestorFor(
        bool isAdmin, SupportTicket ticket, IReadOnlyDictionary<Guid, string?> names)
        => isAdmin
            ? new TicketInvestorDto(
                ticket.InvestorId,
                names.GetValueOrDefault(ticket.InvestorId),
                TicketInvestorDto.ToWireRole(ticket.AuthorRole))
            : null;

    /// <summary>Parses the optional wire status. Returns false only for a non-empty, unrecognized value.</summary>
    private static bool TryParseStatus(string? raw, out TicketStatus? status)
    {
        status = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (Enum.TryParse<TicketStatus>(raw, ignoreCase: true, out var parsed))
        {
            status = parsed;
            return true;
        }

        return false;
    }
}
