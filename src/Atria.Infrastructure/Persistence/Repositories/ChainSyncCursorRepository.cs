using Atria.Application.Abstractions;
using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class ChainSyncCursorRepository : Repository<ChainSyncCursor>, IChainSyncCursorRepository
{
    public ChainSyncCursorRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the sync advances the cursor it finds and persists it in the same unit of work.
    public Task<ChainSyncCursor?> FindByPropertyAsync(Guid propertyId, CancellationToken ct)
        => Set.FirstOrDefaultAsync(c => c.PropertyId == propertyId, ct);
}
