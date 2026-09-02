using Atria.Application.Abstractions;
using Atria.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class OperatingPeriodRepository : Repository<OperatingPeriod>, IOperatingPeriodRepository
{
    public OperatingPeriodRepository(AtriaDbContext db) : base(db) { }

    // Include the breakdown so callers read and mutate the full aggregate (ReplaceLines).
    public override Task<OperatingPeriod?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.Include(p => p.Lines).FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<OperatingPeriod>> ListByPropertyAsync(
        Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(p => p.PropertyId == propertyId)
            .Include(p => p.Lines)
            .OrderByDescending(p => p.EndUtc)
            .ToListAsync(ct);

    public Task<OperatingPeriod?> FindByRangeAsync(
        Guid propertyId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.PropertyId == propertyId
                     && p.StartUtc == startUtc.Date
                     && p.EndUtc == endUtc.Date,
                ct);
}
