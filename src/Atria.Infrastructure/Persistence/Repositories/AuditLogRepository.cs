using Atria.Application.Abstractions;
using Atria.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

/// <summary>
/// Append-only audit store. Not an <see cref="IRepository{TEntity}"/> because
/// <see cref="AuditLogEntry"/> is an immutable, non-aggregate record.
/// </summary>
public sealed class AuditLogRepository : IAuditLogRepository
{
    private readonly AtriaDbContext _db;

    public AuditLogRepository(AtriaDbContext db) => _db = db;

    public async Task AddAsync(AuditLogEntry e, CancellationToken ct)
        => await _db.AuditLogEntries.AddAsync(e, ct);

    public async Task<IReadOnlyList<AuditLogEntry>> QueryAsync(
        string? entityType, Guid? entityId, CancellationToken ct)
    {
        var query = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(a => a.EntityType == entityType);
        if (entityId is not null)
            query = query.Where(a => a.EntityId == entityId);

        return await query.OrderByDescending(a => a.OccurredOnUtc).ToListAsync(ct);
    }
}
