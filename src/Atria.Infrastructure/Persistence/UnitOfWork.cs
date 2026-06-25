using Atria.Application.Abstractions;

namespace Atria.Infrastructure.Persistence;

/// <summary>
/// Commits the current transaction by delegating to the DbContext. The context's
/// SaveChanges override writes raised domain events to the outbox in the same
/// transaction (transactional outbox).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AtriaDbContext _db;

    public UnitOfWork(AtriaDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
