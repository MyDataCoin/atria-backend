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
        // Where a garage / parking space sits in the car park. Strings: sections and rows carry
        // letters ("B", "12А") as often as digits.
        b.Property(p => p.Section).HasMaxLength(Property.MaxParkingAddressPart);
        b.Property(p => p.Row).HasMaxLength(Property.MaxParkingAddressPart);
        b.Property(p => p.Spot).HasMaxLength(Property.MaxParkingAddressPart);
        b.Property(p => p.TotalAreaSqM).HasPrecision(10, 2);
        b.Property(p => p.UsableAreaSqM).HasPrecision(10, 2);
        // Placed area — same precision as the floor areas it is measured against, though it is
        // neither of them: an issue may place more metres than the object has floor today.
        b.Property(p => p.OfferedAreaSqM).HasPrecision(10, 2);

        // Описательные характеристики карточки — «заполняется тем, что есть». Свободный текст:
        // «монолит-кирпич» и «2 пассажирских, 1 грузовой» — реальные ответы, под enum их не свести.
        b.Property(p => p.DocumentedUse).HasMaxLength(256);
        b.Property(p => p.BuildingClass).HasMaxLength(64);
        b.Property(p => p.WallMaterial).HasMaxLength(128);
        b.Property(p => p.Heating).HasMaxLength(128);
        b.Property(p => p.Elevator).HasMaxLength(128);
        b.Property(p => p.Security).HasMaxLength(128);
        b.Property(p => p.Parking).HasMaxLength(256);
        // A paragraph about the district, not a label: longer than the other characteristics and
        // shorter than the object's own description.
        b.Property(p => p.LocationDescription).HasMaxLength(2048);

        // Cadastre and land. Hectares kept in their own column with their own precision: a plot is
        // written to four decimals (0.7200 ha) where floor area is written to two.
        b.Property(p => p.LandPlotCode).HasMaxLength(64);
        b.Property(p => p.CadastralNumber).HasMaxLength(64);
        b.Property(p => p.LandAreaHectares).HasPrecision(12, 4);

        // Construction readiness — orthogonal to Status, which tracks the placement.
        b.Property(p => p.ConstructionStage).HasConversion<int>().IsRequired();
        b.Property(p => p.PlannedCompletionDate);
        b.Property(p => p.ReadinessPercent);
        // How often the issue distributes. `None` is a real answer for an object that does not earn.
        b.Property(p => p.PayoutFrequency).HasConversion<int>().IsRequired();

        // Cadastre check for third-party encumbrances. Nullable bool on purpose: "clean" and
        // "nobody looked" have to stay distinguishable.
        b.Property(p => p.IsFreeOfEncumbrances);
        b.Property(p => p.EncumbranceCheckedAtUtc);
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
        // Целые доли: bigint совпадает с decimals() = 0 у токена, поэтому база не может хранить
        // величину, которую реестр или контракт не смогут представить.
        b.Property(p => p.TotalTokens).IsRequired();
        b.Property(p => p.AvailableTokens).IsRequired();
        b.Property(p => p.MinPurchaseTokens).IsRequired();
        b.Property(p => p.Currency).HasMaxLength(8).IsRequired();
        b.Property(p => p.Status).HasConversion<int>().IsRequired();
        b.Property(p => p.SalesPaused).IsRequired();

        // Placement window. Dates optional — an issue placed by hand has neither; the target is the
        // soft cap the outcome is judged against, and the extension count keeps a pushed-back
        // closing date from being invisible after the fact.
        b.Property(p => p.PlacementOpensAtUtc);
        b.Property(p => p.PlacementClosesAtUtc);
        b.Property(p => p.TargetAmount).HasPrecision(18, 2);
        b.Property(p => p.PlacementExtensionCount).IsRequired();
        // The sweep asks "which issues are due to open or close?" on every tick; without this it
        // reads every property in the table to answer.
        b.HasIndex(p => new { p.Status, p.PlacementOpensAtUtc });
        b.HasIndex(p => new { p.Status, p.PlacementClosesAtUtc });

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
