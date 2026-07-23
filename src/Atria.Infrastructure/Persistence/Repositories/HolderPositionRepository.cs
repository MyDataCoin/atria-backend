using Atria.Application.Abstractions;
using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class HolderPositionRepository : Repository<HolderPosition>, IHolderPositionRepository
{
    public HolderPositionRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the projection handler adjusts and persists the returned position through the unit of work.
    public Task<HolderPosition?> GetByAddressAsync(Guid propertyId, string walletAddress, CancellationToken ct)
        => Set.FirstOrDefaultAsync(p => p.PropertyId == propertyId && p.WalletAddress == walletAddress, ct);

    public async Task<IReadOnlyList<HolderPosition>> GetByPropertyAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(p => p.PropertyId == propertyId)
            .ToListAsync(ct);
}
