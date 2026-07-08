using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PropertyDocumentConfiguration : IEntityTypeConfiguration<PropertyDocument>
{
    public void Configure(EntityTypeBuilder<PropertyDocument> b)
    {
        b.ToTable("property_documents");
        b.HasKey(d => d.Id);

        // Same reasoning as PropertyImageConfiguration: Id is app-assigned and this entity is only
        // ever discovered via Property._documents fixup, never an explicit repository.AddAsync.
        b.Property(d => d.Id).ValueGeneratedNever();

        b.Property(d => d.PropertyId).IsRequired();
        b.Property(d => d.Url).HasMaxLength(1024).IsRequired();
        b.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(256).IsRequired();

        b.HasIndex(d => d.PropertyId);
    }
}
