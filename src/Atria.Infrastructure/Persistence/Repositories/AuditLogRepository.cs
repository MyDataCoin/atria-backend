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

    public async Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> GetPageAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var query = _db.AuditLogEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.EntityType == filter.EntityType);
        if (filter.EntityId is not null)
            query = query.Where(a => a.EntityId == filter.EntityId);
        if (!string.IsNullOrWhiteSpace(filter.EventType))
            query = query.Where(a => a.EventType == filter.EventType);
        if (filter.Severity is { } severity)
            query = query.Where(a => a.Severity == severity);

        var totalCount = await query.CountAsync(ct);

        // The journal grows fastest of everything here, so it is only ever read a page at a time.
        var items = await query
            .OrderByDescending(a => a.OccurredOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
