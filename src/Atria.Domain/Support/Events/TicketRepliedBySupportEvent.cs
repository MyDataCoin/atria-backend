using Atria.Domain.Common;

namespace Atria.Domain.Support.Events;

/// <summary>Raised when support (admin) replies to a ticket — the author gets notified.</summary>
public sealed record TicketRepliedBySupportEvent(
    Guid TicketId,
    Guid AuthorId,
    string Subject) : DomainEventBase;
