namespace Atria.Application.Abstractions;

/// <summary>
/// Commits the current transaction. The implementation also writes raised domain
/// events into the outbox table in the SAME transaction (transactional outbox),
/// so an event is never lost between commit and dispatch.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
