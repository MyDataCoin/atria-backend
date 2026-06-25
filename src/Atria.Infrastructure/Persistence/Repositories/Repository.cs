using Atria.Application.Abstractions;
using Atria.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic EF Core repository over an aggregate root. Specialized repositories
/// derive from this and add aggregate-specific queries. Persistence is committed
/// by the UnitOfWork, not here.
/// </summary>
public class Repository<TEntity> : IRepository<TEntity>
    where TEntity : AggregateRoot
{
    protected readonly AtriaDbContext Db;
    protected DbSet<TEntity> Set => Db.Set<TEntity>();

    public Repository(AtriaDbContext db) => Db = db;

    public virtual Task<TEntity?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.FirstOrDefaultAsync(e => e.Id == id, ct);

    public virtual async Task AddAsync(TEntity entity, CancellationToken ct)
        => await Set.AddAsync(entity, ct);

    public virtual void Update(TEntity entity) => Set.Update(entity);

    public virtual void Remove(TEntity entity) => Set.Remove(entity);
}
