using Atria.Domain.Common;

namespace Atria.Domain.Support.Events;

/// <summary>Raised when a ticket is closed (resolved) — the author gets notified.</summary>
public sealed record TicketClosedEvent(
    Guid TicketId,
    Guid AuthorId,
    string Subject) : DomainEventBase, IExplicitlyAudited;
