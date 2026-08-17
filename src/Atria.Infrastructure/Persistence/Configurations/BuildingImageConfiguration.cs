using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class BuildingImageConfiguration : IEntityTypeConfiguration<BuildingImage>
{
    public void Configure(EntityTypeBuilder<BuildingImage> b)
    {
        b.ToTable("building_images");
        b.HasKey(i => i.Id);

        // Id is assigned in BuildingImage.Create, never by EF/the database — same reason as
        // PropertyImage: the entity is discovered through the parent's backing field, so a
        // generated-on-add key would make EF read the row as pre-existing and UPDATE instead of INSERT.
        b.Property(i => i.Id).ValueGeneratedNever();

        b.Property(i => i.BuildingId).IsRequired();
        b.Property(i => i.Url).HasMaxLength(1024).IsRequired();

        b.HasIndex(i => i.BuildingId);
    }
}
