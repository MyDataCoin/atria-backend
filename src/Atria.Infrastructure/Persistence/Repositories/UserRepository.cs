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

    public async Task<User> EnsureServiceAccountAsync(
        Guid id, Role role, string passwordHash, CancellationToken ct)
    {
        var existing = await Set.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (existing is not null)
        {
            // Backfill a hash for a row inserted by hand without one, so future logins verify by hash.
            if (existing.PasswordHash is null)
            {
                existing.SetPassword(passwordHash, mustReset: false);
                await Db.SaveChangesAsync(ct);
            }
            return existing;
        }

        var account = User.CreateServiceAccount(id, role, passwordHash);
        await Set.AddAsync(account, ct);
        try
        {
            await Db.SaveChangesAsync(ct);
            return account;
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException or InvalidOperationException)
        {
            // A concurrent login inserted the same id first. Relational providers surface this as
            // DbUpdateException (unique-key violation); the EF in-memory provider as an
            // ArgumentException/InvalidOperationException on the duplicate tracked key. Either way the
            // row now exists: drop our pending insert and return the winner's row.
            Db.Entry(account).State = EntityState.Detached;
            var winner = await Set.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (winner is not null)
                return winner;
            throw;
        }
    }
}
