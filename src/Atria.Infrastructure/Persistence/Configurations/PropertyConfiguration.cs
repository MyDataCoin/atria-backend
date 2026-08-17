using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("properties");
        b.HasKey(p => p.Id);

        b.Property(p => p.Name).HasMaxLength(256).IsRequired();
        b.Property(p => p.Description).HasMaxLength(4096);
        b.Property(p => p.Address).HasMaxLength(512);
        b.Property(p => p.PropertyType).HasMaxLength(64);
        b.Property(p => p.City).HasMaxLength(128);
        b.Property(p => p.YearBuilt);
        b.Property(p => p.Developer).HasMaxLength(256);
        b.Property(p => p.Floors);

        // Unit inside a building. Restrict, not Cascade: deleting a building must never silently
        // delete live token issues — the units have to be moved or removed first.
        b.Property(p => p.UnitType).HasConversion<int>().IsRequired();
        b.Property(p => p.UnitNumber).HasMaxLength(32);
        b.Property(p => p.FloorNumber);
        b.Property(p => p.RoomCount);
        b.Property(p => p.TotalAreaSqM).HasPrecision(10, 2);
        b.HasOne<Building>()
            .WithMany()
            .HasForeignKey(p => p.BuildingId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(p => p.BuildingId);

        b.Property(p => p.TotalValue).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.TokenPrice).HasPrecision(18, 2).IsRequired();

        // Collateral and its state registration. All optional: an issue is drafted before it is
        // appraised, pledged and registered.
        b.Property(p => p.CollateralValue).HasPrecision(18, 2);
        b.Property(p => p.CollateralAppraiser).HasMaxLength(256);
        b.Property(p => p.EncumbranceRegistrationNumber).HasMaxLength(128);
        b.Property(p => p.IssueRegistrationNumber).HasMaxLength(128);
        b.Property(p => p.TotalTokens).IsRequired();
        b.Property(p => p.AvailableTokens).IsRequired();
        b.Property(p => p.Currency).HasMaxLength(8).IsRequired();
        b.Property(p => p.Status).HasConversion<int>().IsRequired();
        b.Property(p => p.SalesPaused).IsRequired();

        // On-chain issuance (each property is its own registered issuance / permissioned contract).
        b.Property(p => p.TokenContractAddress).HasMaxLength(128);
        b.Property(p => p.TokenChain).HasMaxLength(64);
        b.Property(p => p.IssuerWalletAddress).HasMaxLength(128);

        // Optimistic concurrency: two concurrent applications racing on the last tokens must not both
        // reserve — the second SaveChanges fails on the changed row and is retried against fresh supply.
        b.XminConcurrencyToken();

        // Child media collections mapped via backing fields, owned by the Property aggregate.
        b.HasMany(p => p.Images)
            .WithOne()
            .HasForeignKey(i => i.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Images)
            .HasField("_images")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasMany(p => p.Documents)
            .WithOne()
            .HasForeignKey(d => d.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Documents)
            .HasField("_documents")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Room breakdown of the unit. ReplaceRooms() clears and refills the collection, so the
        // required FK makes EF delete the dropped rows as orphans instead of orphaning them.
        b.HasMany(p => p.Rooms)
            .WithOne()
            .HasForeignKey(r => r.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Rooms)
            .HasField("_rooms")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
