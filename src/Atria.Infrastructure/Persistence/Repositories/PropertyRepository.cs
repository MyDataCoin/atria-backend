using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class PropertyRepository : Repository<Property>, IPropertyRepository
{
    public PropertyRepository(AtriaDbContext db) : base(db) { }

    // Include media so callers can read/mutate the full aggregate (add/remove images & documents).
    public override Task<Property?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.Include(p => p.Images)
              .Include(p => p.Documents)
              .Include(p => p.Rooms)
              .FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Property>> GetAllAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Include(p => p.Images)
            .Include(p => p.Documents)
            .Include(p => p.Rooms)
            .ToListAsync(ct);

    // Tracked, and deliberately without the media includes: the sweep only moves a status, and
    // dragging every image and room of every due issue through the change tracker would make the
    // cheapest query in the system one of the most expensive.
    public async Task<IReadOnlyList<Property>> GetDuePlacementsAsync(
        DateTime asOfUtc, int batchSize, CancellationToken ct)
        => await Set
            .Where(p =>
                ((p.Status == PropertyStatus.Draft || p.Status == PropertyStatus.ComingSoon)
                    && p.PlacementOpensAtUtc != null && p.PlacementOpensAtUtc <= asOfUtc)
                || (p.Status == PropertyStatus.Open
                    && p.PlacementClosesAtUtc != null && p.PlacementClosesAtUtc <= asOfUtc))
            .OrderBy(p => p.PlacementClosesAtUtc ?? p.PlacementOpensAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Property>> GetByBuildingAsync(Guid buildingId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(p => p.BuildingId == buildingId)
            .Include(p => p.Images)
            .Include(p => p.Documents)
            .Include(p => p.Rooms)
            .ToListAsync(ct);
}
