using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class BlockchainOperationRepository
    : Repository<BlockchainOperation>, IBlockchainOperationRepository
{
    public BlockchainOperationRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<BlockchainOperation>> ListAsync(
        BlockchainOperationStatus? status, int limit, CancellationToken ct)
    {
        var query = Set.AsNoTracking().AsQueryable();

        if (status is { } wanted)
            query = query.Where(o => o.Status == wanted);

        // Newest first, and capped: the queue is append-only and an operator looking for what just
        // broke reads the top of it, never the whole history.
        return await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Take(limit)
            .ToListAsync(ct);
    }
}
