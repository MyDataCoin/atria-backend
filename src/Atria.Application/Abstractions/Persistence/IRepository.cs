using Atria.Domain.Common;

namespace Atria.Application.Abstractions;

/// <summary>
/// Generic aggregate repository. Specialized repositories extend this with
/// query methods specific to their aggregate. The Application layer never sees
/// DbContext.
/// </summary>
public interface IRepository<TEntity>
    where TEntity : AggregateRoot
{
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(TEntity entity, CancellationToken ct);
    void Update(TEntity entity);
    void Remove(TEntity entity);
}
