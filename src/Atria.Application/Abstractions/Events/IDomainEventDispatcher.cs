using Atria.Domain.Common;

namespace Atria.Application.Abstractions;

/// <summary>
/// Resolves and invokes all <see cref="IDomainEventHandler{TEvent}"/> for the given
/// events. Called by the outbox background dispatcher, not inline after SaveChanges.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct);

    /// <summary>Dispatch a single event deserialized from an outbox row.</summary>
    Task DispatchAsync(IDomainEvent domainEvent, CancellationToken ct);
}
