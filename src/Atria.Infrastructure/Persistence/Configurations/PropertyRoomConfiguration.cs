using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PropertyRoomConfiguration : IEntityTypeConfiguration<PropertyRoom>
{
    public void Configure(EntityTypeBuilder<PropertyRoom> b)
    {
        b.ToTable("property_rooms");
        b.HasKey(r => r.Id);

        // Id is assigned in PropertyRoom.Create, never by EF/the database (see PropertyImage).
        b.Property(r => r.Id).ValueGeneratedNever();

        b.Property(r => r.PropertyId).IsRequired();
        b.Property(r => r.Name).HasMaxLength(128).IsRequired();
        b.Property(r => r.AreaSqM).HasPrecision(10, 2).IsRequired();
        b.Property(r => r.SortOrder).IsRequired();

        b.HasIndex(r => r.PropertyId);
    }
}
