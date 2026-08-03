using Atria.Domain.Regulatory;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="RegulatoryReport"/>, the register of filing obligations.</summary>
public interface IRegulatoryReportRepository : IRepository<RegulatoryReport>
{
    /// <summary>
    /// The obligation already recorded for this kind, issue and period, or null. Keeps the periodic
    /// sweep from raising the same monthly or quarterly notification twice.
    /// </summary>
    Task<RegulatoryReport?> FindAsync(
        RegulatoryReportKind kind, Guid? propertyId, DateTime periodEndUtc, CancellationToken ct);

    /// <summary>Obligations not yet filed, earliest deadline first — the reminder queue.</summary>
    Task<IReadOnlyList<RegulatoryReport>> ListOutstandingAsync(CancellationToken ct);

    /// <summary>Every obligation, newest period first, optionally narrowed to one issue.</summary>
    Task<IReadOnlyList<RegulatoryReport>> ListAsync(Guid? propertyId, int take, CancellationToken ct);
}
