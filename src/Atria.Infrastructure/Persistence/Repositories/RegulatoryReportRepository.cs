using Atria.Application.Abstractions;
using Atria.Domain.Regulatory;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class RegulatoryReportRepository : Repository<RegulatoryReport>, IRegulatoryReportRepository
{
    public RegulatoryReportRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the sweep and the generate command mutate what they find.
    public Task<RegulatoryReport?> FindAsync(
        RegulatoryReportKind kind, Guid? propertyId, DateTime periodEndUtc, CancellationToken ct)
        => Set.FirstOrDefaultAsync(
            r => r.Kind == kind && r.PropertyId == propertyId && r.PeriodEndUtc == periodEndUtc, ct);

    public async Task<IReadOnlyList<RegulatoryReport>> ListOutstandingAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(r => r.Status != RegulatoryReportStatus.Filed)
            .OrderBy(r => r.DueAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RegulatoryReport>> ListAsync(Guid? propertyId, int take, CancellationToken ct)
    {
        var query = Set.AsNoTracking().AsQueryable();
        if (propertyId is not null)
            query = query.Where(r => r.PropertyId == propertyId);

        return await query
            .OrderByDescending(r => r.PeriodEndUtc)
            .Take(take)
            .ToListAsync(ct);
    }
}
