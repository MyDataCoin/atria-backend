using Atria.Application.Abstractions;
using Atria.Infrastructure.Persistence;
using Atria.Infrastructure.Persistence.Seeding;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atria.Application.Tests.Persistence;

/// <summary>Covers the demo tokenization-object seeder: seeds when empty, idempotent on re-run.</summary>
public sealed class DataSeederTests
{
    private static AtriaDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AtriaDbContext>()
                .UseInMemoryDatabase($"seed-{Guid.NewGuid()}")
                .Options,
            new NoOpEncryption());

    [Fact]
    public async Task SeedAsync_when_empty_inserts_demo_properties()
    {
        await using var db = NewDb();

        await DataSeeder.SeedAsync(db, NullLogger.Instance);

        var properties = await db.Properties.ToListAsync();
        properties.Should().HaveCountGreaterThan(0);
        properties.Should().OnlyContain(p => p.IsActive);
        properties.Should().OnlyContain(p => p.AvailableTokens == p.TotalTokens);
        properties.Should().OnlyContain(p => p.TokenPrice > 0 && p.TotalValue > 0);
    }

    [Fact]
    public async Task SeedAsync_is_idempotent_on_rerun()
    {
        await using var db = NewDb();

        await DataSeeder.SeedAsync(db, NullLogger.Instance);
        var afterFirst = await db.Properties.CountAsync();

        await DataSeeder.SeedAsync(db, NullLogger.Instance);
        var afterSecond = await db.Properties.CountAsync();

        afterSecond.Should().Be(afterFirst, "re-running the seeder must not duplicate rows");
    }

    private sealed class NoOpEncryption : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
