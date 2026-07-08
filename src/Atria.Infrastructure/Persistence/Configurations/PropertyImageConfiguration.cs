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

        // Id is assigned in PropertyImage.Create (Guid.NewGuid()), never by EF/the database. Without
        // this, EF's default convention for Guid keys is ValueGeneratedOnAdd; since this entity is
        // discovered only via Property._images fixup (never an explicit repository.AddAsync), EF sees
        // an already-non-default key on a newly-tracked entity and infers it pre-exists in the
        // database, tracking it as Unchanged/Modified instead of Added -> an UPDATE hits 0 rows
        // (DbUpdateConcurrencyException) instead of an INSERT.
        b.Property(i => i.Id).ValueGeneratedNever();

        b.Property(i => i.PropertyId).IsRequired();
        b.Property(i => i.Url).HasMaxLength(1024).IsRequired();

        b.HasIndex(i => i.PropertyId);
    }
}
