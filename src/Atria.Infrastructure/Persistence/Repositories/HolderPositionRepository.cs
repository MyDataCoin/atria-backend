using Atria.Application.Abstractions;
using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class HolderPositionRepository : Repository<HolderPosition>, IHolderPositionRepository
{
    public HolderPositionRepository(AtriaDbContext db) : base(db) { }

    // Tracked: the projection handler adjusts and persists the returned position through the unit of work.
    /// <remarks>
    /// Case-insensitive for the same reason the compliance lookup is: our own records hold the
    /// address as the investor submitted it (EIP-55 mixed case), chain events yield lowercase hex,
    /// and an exact comparison would give one holder two positions in the register — which then
    /// splits their holding across two lines of a distribution.
    /// </remarks>
    public Task<HolderPosition?> GetByAddressAsync(Guid propertyId, string walletAddress, CancellationToken ct)
        => Set.Where(p => p.PropertyId == propertyId
                          && p.WalletAddress.ToLower() == walletAddress.ToLower())
            // Deterministic when case-variant rows already exist: the unique index is case-sensitive,
            // so both forms can be sitting in the table right now.
            .OrderBy(p => p.WalletAddress)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<HolderPosition>> GetByPropertyAsync(Guid propertyId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(p => p.PropertyId == propertyId)
            .ToListAsync(ct);
}
