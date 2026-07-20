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

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct)
        => Set.FirstOrDefaultAsync(u => u.Username == username, ct);

    public Task<int> CountByRoleAsync(Role role, CancellationToken ct)
        => Set.AsNoTracking().CountAsync(u => u.Role == role && u.DeletedAtUtc == null, ct);

    public async Task<IReadOnlyList<Guid>> GetIdsByRoleAsync(Role role, CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(u => u.Role == role && u.DeletedAtUtc == null)
            .Select(u => u.Id)
            .ToListAsync(ct);

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

    public async Task<IReadOnlyList<User>> GetStaffAsync(CancellationToken ct)
        => await Set.AsNoTracking()
            .Where(u => (u.Role == Role.Admin || u.Role == Role.SuperAdmin) && u.DeletedAtUtc == null)
            .OrderByDescending(u => u.CreatedAtUtc)
            .ToListAsync(ct);
}
