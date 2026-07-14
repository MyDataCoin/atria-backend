using Atria.Domain.Audit;

namespace Atria.Application.Abstractions;

/// <summary>Filters for an audit-journal query. All optional, combined with AND.</summary>
/// <param name="EntityType">Scope to one aggregate kind (e.g. <c>Property</c>).</param>
/// <param name="EntityId">Scope to one instance.</param>
/// <param name="EventType">Scope to one action (e.g. <c>PropertyPublished</c>).</param>
/// <param name="Severity">Scope to one criticality level.</param>
public sealed record AuditLogFilter(
    string? EntityType,
    Guid? EntityId,
    string? EventType,
    AuditSeverity? Severity);

/// <summary>
/// Append-only store for audit log entries. Bespoke (not an
/// <see cref="IRepository{TEntity}"/>) because <see cref="AuditLogEntry"/> is an
/// immutable, non-aggregate record: it is only ever added and queried — never updated or deleted.
/// </summary>
public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry e, CancellationToken ct);

    /// <summary>One page of the journal, newest first, plus the total count across all pages.</summary>
    Task<(IReadOnlyList<AuditLogEntry> Items, int TotalCount)> GetPageAsync(
        AuditLogFilter filter, int page, int pageSize, CancellationToken ct);
}
