using Atria.Application.Abstractions;
using Atria.Domain.Kyc;
using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AtriaDbContext db) : base(db) { }

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct)
        => Set.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

    public Task<int> CountByRoleAsync(Role role, CancellationToken ct)
        => Set.AsNoTracking().CountAsync(u => u.Role == role && u.DeletedAtUtc == null, ct);

    public async Task<IReadOnlyList<(User User, KycProfile? Kyc)>> GetOverviewAsync(CancellationToken ct)
    {
        // LEFT JOIN users -> kyc_profiles. Materializing the KycProfile entity (not projecting
        // its raw column) is what lets the EF value converter decrypt FullName in-memory.
        var rows = await (
            from u in Db.Users.AsNoTracking()
            join k in Db.KycProfiles.AsNoTracking() on u.Id equals k.UserId into grp
            from k in grp.DefaultIfEmpty()
            orderby u.CreatedAtUtc descending
            select new { u, k }).ToListAsync(ct);

        return rows.Select(r => (r.u, (KycProfile?)r.k)).ToList();
    }
}
