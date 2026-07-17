using Atria.Application.Abstractions;
using Atria.Domain.Users;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atria.Application.Tests.Persistence;

/// <summary>
/// Covers the service-account seeder that creates the Admin/Realtor/SuperAdmin <c>users</c> rows.
/// The critical case is that a MISCONFIGURATION (two roles sharing a UserId) must not crash startup
/// with a duplicate-primary-key insert — that would take the whole API down (502 on every request).
/// </summary>
public sealed class ServiceAccountSeederTests
{
    private static AtriaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AtriaDbContext>()
                .UseInMemoryDatabase($"svc-seed-{Guid.NewGuid()}")
                .Options,
            new NoOpEncryption());

    private static ServiceAccountSeeder Seeder(
        (string user, string pass, Guid id) admin,
        (string user, string pass, Guid id) realtor,
        (string user, string pass, Guid id) super)
        => new(
            Options.Create(new AdminOptions { Username = admin.user, Password = admin.pass, UserId = admin.id }),
            Options.Create(new RealtorOptions { Username = realtor.user, Password = realtor.pass, UserId = realtor.id }),
            Options.Create(new SuperAdminOptions { Username = super.user, Password = super.pass, UserId = super.id }),
            new PlainHasher());

    [Fact]
    public async Task Seeds_a_row_per_configured_account_with_role_and_hash()
    {
        await using var db = NewDb();
        var adminId = Guid.NewGuid();
        var realtorId = Guid.NewGuid();
        var superId = Guid.NewGuid();

        await Seeder(
            ("admin", "admin-pass", adminId),
            ("realtor", "realtor-pass", realtorId),
            ("superadmin", "super-pass", superId)).SeedAsync(db, NullLogger.Instance);

        var users = await db.Users.ToListAsync();
        users.Should().HaveCount(3);
        users.Single(u => u.Id == adminId).Role.Should().Be(Role.Admin);
        users.Single(u => u.Id == realtorId).Role.Should().Be(Role.Realtor);
        users.Single(u => u.Id == superId).Role.Should().Be(Role.SuperAdmin);
        users.Should().OnlyContain(u => u.PasswordHash != null);
    }

    [Fact]
    public async Task Skips_accounts_without_a_configured_password()
    {
        await using var db = NewDb();
        var superId = Guid.NewGuid();

        await Seeder(
            ("admin", "", Guid.NewGuid()),          // no password => disabled, not seeded
            ("realtor", "", Guid.NewGuid()),
            ("superadmin", "super-pass", superId)).SeedAsync(db, NullLogger.Instance);

        var users = await db.Users.ToListAsync();
        users.Should().ContainSingle().Which.Id.Should().Be(superId);
    }

    [Fact]
    public async Task Colliding_user_ids_do_not_crash_and_seed_only_once()
    {
        await using var db = NewDb();
        var shared = Guid.NewGuid();

        // Admin and SuperAdmin both configured with the SAME id — the seeder must NOT try to insert
        // two rows with the same primary key (which would throw and crash boot).
        var act = async () => await Seeder(
            ("admin", "admin-pass", shared),
            ("realtor", "", Guid.NewGuid()),
            ("superadmin", "super-pass", shared)).SeedAsync(db, NullLogger.Instance);

        await act.Should().NotThrowAsync();
        (await db.Users.CountAsync()).Should().Be(1, "the shared id is claimed once");
    }

    [Fact]
    public async Task Is_idempotent_on_rerun()
    {
        await using var db = NewDb();
        var ids = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var seeder = Seeder(
            ("admin", "admin-pass", ids.Item1),
            ("realtor", "realtor-pass", ids.Item2),
            ("superadmin", "super-pass", ids.Item3));

        await seeder.SeedAsync(db, NullLogger.Instance);
        await seeder.SeedAsync(db, NullLogger.Instance);

        (await db.Users.CountAsync()).Should().Be(3);
    }

    private sealed class NoOpEncryption : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }

    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    }
}
