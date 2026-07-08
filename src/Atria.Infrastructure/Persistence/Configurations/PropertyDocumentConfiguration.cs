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

        b.Property(d => d.PropertyId).IsRequired();
        b.Property(d => d.Url).HasMaxLength(1024).IsRequired();
        b.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(256).IsRequired();

        b.HasIndex(d => d.PropertyId);
    }
}
