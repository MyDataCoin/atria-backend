using Atria.Application.Abstractions;
using Atria.Domain.Investments;
using Atria.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atria.Application.Tests.Persistence;

/// <summary>
/// PropertyImage/PropertyDocument are never added via an explicit repository.AddAsync — EF only
/// discovers them through Property._images/._documents fixup on an already-tracked, already-
/// persisted Property. With a client-assigned Guid key and no ValueGeneratedNever(), EF's default
/// convention for Guid keys can mistake that for a pre-existing row and track it as
/// Modified/Unchanged instead of Added, turning the eventual save into an UPDATE that matches
/// zero rows (DbUpdateConcurrencyException in production; caught here via entity state, since
/// entity-state determination is part of EF Core's provider-agnostic change tracker).
/// </summary>
public sealed class PropertyMediaTrackingTests
{
    private static AtriaDbContext NewDb(string name) =>
        new(new DbContextOptionsBuilder<AtriaDbContext>()
                .UseInMemoryDatabase(name)
                .Options,
            new NoOpEncryption());

    [Fact]
    public async Task AddImage_on_a_freshly_loaded_property_is_tracked_as_added_and_persists()
    {
        var dbName = $"media-{Guid.NewGuid()}";

        var property = Property.Create("Test", null, null, 100_000m, 100m, 1000, "USD");
        await using (var seedDb = NewDb(dbName))
        {
            seedDb.Properties.Add(property);
            await seedDb.SaveChangesAsync();
        }

        // A separate DbContext simulates the next request loading the aggregate fresh.
        await using (var db = NewDb(dbName))
        {
            var loaded = await db.Properties.Include(p => p.Images).FirstAsync(p => p.Id == property.Id);
            var image = loaded.AddImage("https://example.test/photo.jpg");

            // DetectChanges is what actually walks the graph to discover the new child and
            // classify it (SaveChangesAsync triggers this internally; forced explicitly here so
            // the assertion below reflects the real classification instead of Detached).
            db.ChangeTracker.DetectChanges();
            db.Entry(image).State.Should().Be(
                EntityState.Added,
                "a brand-new child discovered via collection fixup must be tracked as Added, not Modified/Unchanged");

            await db.SaveChangesAsync();
        }

        await using var verifyDb = NewDb(dbName);
        var reloaded = await verifyDb.Properties.Include(p => p.Images).FirstAsync(p => p.Id == property.Id);
        reloaded.Images.Should().ContainSingle(i => i.Url == "https://example.test/photo.jpg");
    }

    private sealed class NoOpEncryption : IEncryptionService
    {
        public string Encrypt(string plaintext) => plaintext;
        public string Decrypt(string ciphertext) => ciphertext;
    }
}
