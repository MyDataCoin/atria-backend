namespace Atria.Domain.Common;

/// <summary>
/// Marker for domain events. Carries a stable id (used as the idempotency key
/// for outbox processing and exactly-once effects) and an occurrence timestamp.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}
