using Atria.Domain.Common;

namespace Atria.Application.Abstractions;

/// <summary>
/// Reacts to a domain event. This is the ONLY way modules talk to each other.
/// Handlers must be idempotent: the outbox delivers at-least-once.
/// </summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken ct);
}
