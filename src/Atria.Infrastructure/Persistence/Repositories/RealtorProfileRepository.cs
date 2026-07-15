using Atria.Application.Abstractions;
using Atria.Domain.Deals;
using Atria.Domain.Realtors;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class RealtorProfileRepository : Repository<RealtorProfile>, IRealtorProfileRepository
{
    public RealtorProfileRepository(AtriaDbContext db) : base(db) { }

    public Task<RealtorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct)
        => Set.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task<IReadOnlyList<(Guid UserId, string FullName, string? CompanyName, int ClosedDeals, int TotalDeals, bool Blocked)>>
        GetStatsAsync(CancellationToken ct)
    {
        // Profiles drive the row set so realtors with zero deals still appear (left join). Deals link to
        // the realtor by user id (Deal.RealtorId == RealtorProfile.UserId); counts are aggregated DB-side.
        // Blocked comes from the users row (left-joined) so a banned realtor shows as blocked.
        var rows = await (
            from p in Db.RealtorProfiles.AsNoTracking()
            join u in Db.Users.AsNoTracking() on p.UserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            select new
            {
                p.UserId,
                p.FullName,
                p.CompanyName,
                ClosedDeals = Db.Deals.Count(d => d.RealtorId == p.UserId && d.Status == DealStatus.Successful),
                TotalDeals = Db.Deals.Count(d => d.RealtorId == p.UserId),
                Blocked = u != null && u.IsBanned
            })
            .OrderByDescending(r => r.ClosedDeals)
            .ThenByDescending(r => r.TotalDeals)
            .ToListAsync(ct);

        return rows
            .Select(r => (r.UserId, r.FullName, r.CompanyName, r.ClosedDeals, r.TotalDeals, r.Blocked))
            .ToList();
    }
}
