using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class BuildingRepository : Repository<Building>, IBuildingRepository
{
    public BuildingRepository(AtriaDbContext db) : base(db) { }

    // Include photos so callers can read/mutate the full aggregate (add/remove images).
    public override Task<Building?> GetByIdAsync(Guid id, CancellationToken ct)
        => Set.Include(x => x.Images).FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Building>> GetAllAsync(CancellationToken ct)
        => await Set.AsNoTracking().Include(x => x.Images).ToListAsync(ct);

    public Task<bool> HasUnitsAsync(Guid buildingId, CancellationToken ct)
        => Db.Properties.AnyAsync(p => p.BuildingId == buildingId, ct);
}
