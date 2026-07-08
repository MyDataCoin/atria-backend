using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PropertyImageConfiguration : IEntityTypeConfiguration<PropertyImage>
{
    public void Configure(EntityTypeBuilder<PropertyImage> b)
    {
        b.ToTable("property_images");
        b.HasKey(i => i.Id);

        b.Property(i => i.PropertyId).IsRequired();
        b.Property(i => i.Url).HasMaxLength(1024).IsRequired();

        b.HasIndex(i => i.PropertyId);
    }
}
