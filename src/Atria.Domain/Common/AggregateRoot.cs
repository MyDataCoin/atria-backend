namespace Atria.Domain.Common;

/// <summary>
/// Aggregate root: an entity that owns a consistency boundary and records
/// domain events. Events are the ONLY channel between modules. They are picked
/// up by the UnitOfWork and persisted to the transactional outbox in the same
/// transaction as the aggregate (see Infrastructure).
/// </summary>
public abstract class AggregateRoot : Entity
{
    private readonly List<IDomainEvent> _domainEvents = new();

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void RaiseEvent(IDomainEvent @event) => _domainEvents.Add(@event);

    public void ClearEvents() => _domainEvents.Clear();
}
