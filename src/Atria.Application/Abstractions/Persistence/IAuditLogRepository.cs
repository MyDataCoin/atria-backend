using Atria.Domain.Audit;

namespace Atria.Application.Abstractions;

/// <summary>
/// Append-only store for audit log entries. Bespoke (not an
/// <see cref="IRepository{TEntity}"/>) because <see cref="AuditLogEntry"/> is an
/// immutable, non-aggregate record: it is only ever added and queried.
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry e, CancellationToken ct);

    Task<IReadOnlyList<AuditLogEntry>> QueryAsync(string? entityType, Guid? entityId, CancellationToken ct);
}
