using Atria.Application.Abstractions;
using Atria.Domain.Appeals;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class AppealRepository : Repository<Appeal>, IAppealRepository
{
    public AppealRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<(Appeal Appeal, string? FullName)>> GetAllWithNamesAsync(CancellationToken ct)
    {
        // Resolve the appellant's name best-effort: appeal.Username -> users.Username -> the linked
        // realtor profile's FullName. Admins have no profile, and an unknown username won't match, so
        // FullName is null in those cases. Left joins keep every appeal in the result.
        var rows = await (
            from a in Db.Appeals.AsNoTracking()
            join u in Db.Users.AsNoTracking() on a.Username equals u.Username into uj
            from u in uj.DefaultIfEmpty()
            join p in Db.RealtorProfiles.AsNoTracking() on u.Id equals p.UserId into pj
            from p in pj.DefaultIfEmpty()
            orderby a.CreatedAtUtc descending
            select new { a, FullName = p != null ? p.FullName : null })
            .ToListAsync(ct);

        return rows.Select(r => (r.a, r.FullName)).ToList();
    }
}
