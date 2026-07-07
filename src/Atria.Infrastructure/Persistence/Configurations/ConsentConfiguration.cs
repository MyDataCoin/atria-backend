using Atria.Domain.Consents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> b)
    {
        b.ToTable("consents");
        b.HasKey(c => c.Id);

        b.Property(c => c.UserId).IsRequired();
        b.Property(c => c.Type).HasConversion<int>().IsRequired();
        b.Property(c => c.Version).HasMaxLength(32).IsRequired();

        b.HasIndex(c => c.UserId);
        // One acceptance row per user + type + version (dedupe + fast enforcement lookup).
        b.HasIndex(c => new { c.UserId, c.Type, c.Version }).IsUnique();
    }
}
