using Atria.Domain.Publications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> b)
    {
        b.ToTable("publications");
        b.HasKey(p => p.Id);

        b.Property(p => p.Type).HasConversion<int>().IsRequired();
        b.Property(p => p.Title).HasMaxLength(Publication.MaxTitleLength).IsRequired();
        b.Property(p => p.Body).HasMaxLength(Publication.MaxBodyLength).IsRequired();
        // Nullable on purpose: a null property means a general, platform-wide news item.
        b.Property(p => p.PropertyId);
        b.Property(p => p.Status).HasConversion<int>().IsRequired();
        b.Property(p => p.PublishedAtUtc).IsRequired();
        b.Property(p => p.AuthorId).IsRequired();

        // The feed is ordered by PublishedAtUtc DESC and filtered by property / status.
        b.HasIndex(p => p.PublishedAtUtc);
        b.HasIndex(p => p.PropertyId);
        b.HasIndex(p => p.Status);

        b.XminConcurrencyToken();
    }
}
