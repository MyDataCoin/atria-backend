using Atria.Application.Abstractions;
using Atria.Domain.Deals;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class DealRepository : Repository<Deal>, IDealRepository
{
    public DealRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Deal>> GetByRealtorAsync(Guid realtorId, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(d => d.RealtorId == realtorId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .ToListAsync(ct);

    public Task<Deal?> GetByReferralTokenAsync(string referralToken, CancellationToken ct)
        => Set.FirstOrDefaultAsync(d => d.ReferralToken == referralToken, ct);

    public async Task<IReadOnlyList<Deal>> GetExpiredPendingAsync(DateTime asOfUtc, CancellationToken ct)
        => await Set
            .Where(d => d.Status == DealStatus.Pending && d.ExpiresAtUtc <= asOfUtc)
            .OrderBy(d => d.ExpiresAtUtc)
            .ToListAsync(ct);
}
