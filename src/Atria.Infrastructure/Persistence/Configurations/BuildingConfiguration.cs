using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class BuildingConfiguration : IEntityTypeConfiguration<Building>
{
    public void Configure(EntityTypeBuilder<Building> b)
    {
        b.ToTable("buildings");
        b.HasKey(x => x.Id);

        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasMaxLength(4096);
        b.Property(x => x.Address).HasMaxLength(512);
        b.Property(x => x.City).HasMaxLength(128);
        b.Property(x => x.Developer).HasMaxLength(256);
        b.Property(x => x.BuildingType).HasMaxLength(64);

        // Child photos mapped via the backing field, owned by the Building aggregate.
        b.HasMany(x => x.Images)
            .WithOne()
            .HasForeignKey(i => i.BuildingId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Images)
            .HasField("_images")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
