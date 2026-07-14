using Atria.Domain.Common;

namespace Atria.Domain.Support.Events;

/// <summary>Raised when a client (investor or realtor) opens a support ticket.</summary>
public sealed record TicketOpenedEvent(
    Guid TicketId,
    Guid AuthorId,
    string Subject) : DomainEventBase;
