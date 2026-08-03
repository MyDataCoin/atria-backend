using Atria.Application.Abstractions;
using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class HolderSnapshotRepository : Repository<HolderSnapshot>, IHolderSnapshotRepository
{
    public HolderSnapshotRepository(AtriaDbContext db) : base(db) { }

    public Task<HolderSnapshot?> FindByCutAsync(
        Guid propertyId, DateTime snapshotAtUtc, SnapshotPurpose purpose, CancellationToken ct)
        => Set.AsNoTracking()
            .FirstOrDefaultAsync(
                s => s.PropertyId == propertyId && s.SnapshotAtUtc == snapshotAtUtc && s.Purpose == purpose,
                ct);

    // Snapshots are immutable, so reads are untracked throughout.
    public Task<HolderSnapshot?> GetWithRowsAsync(Guid id, CancellationToken ct)
        => Set.AsNoTracking()
            .Include(s => s.Rows)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<HolderSnapshot>> ListByPropertyAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(s => s.PropertyId == propertyId)
            .OrderByDescending(s => s.SnapshotAtUtc)
            .ToListAsync(ct);
}
