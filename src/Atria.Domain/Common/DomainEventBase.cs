namespace Atria.Domain.Common;

/// <summary>
/// Convenience base for domain events. Records give value equality and an
/// easy serialization shape for the outbox.
/// </summary>
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
