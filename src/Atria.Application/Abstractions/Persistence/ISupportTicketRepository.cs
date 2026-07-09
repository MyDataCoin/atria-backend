using Atria.Domain.Support;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for support tickets. List queries return tickets WITHOUT their message
/// threads (kept light for the list route); the detail query eager-loads the ordered thread.
/// </summary>
public interface ISupportTicketRepository : IRepository<SupportTicket>
{
    /// <summary>All tickets (Admin scope), optionally filtered by status, newest activity first, no messages.</summary>
    Task<IReadOnlyList<SupportTicket>> GetAllAsync(TicketStatus? status, CancellationToken ct);

    /// <summary>One investor's tickets, optionally filtered by status, newest activity first, no messages.</summary>
    Task<IReadOnlyList<SupportTicket>> GetByInvestorAsync(Guid investorId, TicketStatus? status, CancellationToken ct);

    /// <summary>A single ticket with its message thread (ordered oldest first), or null.</summary>
    Task<SupportTicket?> GetByIdWithMessagesAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Maps investor ids to their decrypted KYC full name (null when unset or no profile), for the
    /// Admin panel to show who opened a ticket. Batched to avoid N+1 over a list.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string?>> GetInvestorNamesAsync(
        IReadOnlyCollection<Guid> investorIds, CancellationToken ct);
}
