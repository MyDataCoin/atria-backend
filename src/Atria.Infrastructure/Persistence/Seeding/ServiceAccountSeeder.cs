using Atria.Application.Abstractions;
using Atria.Domain.Users;
using Atria.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Persistence.Seeding;

/// <summary>
/// Seeds the credential-login service accounts (Admin, Realtor, SuperAdmin) as real <c>users</c>
/// rows keyed by their configured ids, with the configured password hashed. This is what lets the
/// super admin target them by <c>users.id</c> for password reset, and lets login verify against a
/// stored hash. Idempotent per account: inserts only when the id is absent, so it never overwrites a
/// password that was later changed. Each account is skipped when its password is not configured.
/// </summary>
public sealed class ServiceAccountSeeder
{
    private readonly AdminOptions _admin;
    private readonly RealtorOptions _realtor;
    private readonly SuperAdminOptions _superAdmin;
    private readonly IPasswordHasher _hasher;

    public ServiceAccountSeeder(
        IOptions<AdminOptions> admin,
        IOptions<RealtorOptions> realtor,
        IOptions<SuperAdminOptions> superAdmin,
        IPasswordHasher hasher)
    {
        _admin = admin.Value;
        _realtor = realtor.Value;
        _superAdmin = superAdmin.Value;
        _hasher = hasher;
    }

    public async Task SeedAsync(AtriaDbContext db, ILogger logger, CancellationToken ct = default)
    {
        // Ids already claimed in THIS pass, so two roles sharing a configured UserId (e.g. both left
        // at the same GUID) don't try to insert the same primary key twice — the second AnyAsync
        // query wouldn't see the first's pending insert, and SaveChanges would throw.
        var claimed = new HashSet<Guid>();

        // A misconfiguration (colliding ids, a bad row) must NEVER take the API down on boot — the
        // seed is best-effort. Log and continue; login simply falls back to the config password.
        try
        {
            var seeded = 0;
            seeded += await EnsureAsync(db, _admin.UserId, Role.Admin, _admin.Password, claimed, logger, ct);
            seeded += await EnsureAsync(db, _realtor.UserId, Role.Realtor, _realtor.Password, claimed, logger, ct);
            seeded += await EnsureAsync(db, _superAdmin.UserId, Role.SuperAdmin, _superAdmin.Password, claimed, logger, ct);

            if (seeded > 0)
            {
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Seeded {Count} service account(s).", seeded);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Service account seeding failed; continuing startup.");
        }
    }

    private async Task<int> EnsureAsync(
        AtriaDbContext db, Guid id, Role role, string password,
        HashSet<Guid> claimed, ILogger logger, CancellationToken ct)
    {
        // No password configured => the login for this role is disabled; nothing to seed.
        if (id == Guid.Empty || string.IsNullOrEmpty(password))
            return 0;

        if (!claimed.Add(id))
        {
            logger.LogWarning(
                "Service account for {Role} shares UserId {UserId} with another role; skipping. " +
                "Give each configured account a distinct UserId.", role, id);
            return 0;
        }

        if (await db.Users.AnyAsync(u => u.Id == id, ct))
            return 0;

        var account = User.CreateServiceAccount(id, role, _hasher.Hash(password));
        await db.Users.AddAsync(account, ct);
        return 1;
    }
}
