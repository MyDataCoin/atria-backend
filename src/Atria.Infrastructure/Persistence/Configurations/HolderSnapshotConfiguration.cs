using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class HolderSnapshotConfiguration : IEntityTypeConfiguration<HolderSnapshot>
{
    public void Configure(EntityTypeBuilder<HolderSnapshot> b)
    {
        b.ToTable("holder_snapshots");
        b.HasKey(s => s.Id);

        b.Property(s => s.PropertyId).IsRequired();
        b.Property(s => s.SnapshotAtUtc).IsRequired();
        b.Property(s => s.Purpose).HasConversion<int>().IsRequired();
        b.Property(s => s.BlockNumber);
        b.Property(s => s.TotalTokens).IsRequired();
        b.Property(s => s.AddressCount).IsRequired();
        b.Property(s => s.CreatedByUserId).IsRequired();

        b.HasIndex(s => new { s.PropertyId, s.SnapshotAtUtc });

        // Immutable once created; no concurrency token needed (never updated).
        // Row lines owned by the snapshot aggregate, mapped via the backing field.
        b.HasMany(s => s.Rows)
            .WithOne()
            .HasForeignKey(r => r.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(s => s.Rows)
            .HasField("_rows")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class HolderSnapshotRowConfiguration : IEntityTypeConfiguration<HolderSnapshotRow>
{
    public void Configure(EntityTypeBuilder<HolderSnapshotRow> b)
    {
        b.ToTable("holder_snapshot_rows");
        b.HasKey(r => r.Id);

        b.Property(r => r.SnapshotId).IsRequired();
        b.Property(r => r.WalletAddress).HasMaxLength(128).IsRequired();
        b.Property(r => r.TokenCount).IsRequired();
        b.Property(r => r.InvestorId);
        b.Property(r => r.Share).HasPrecision(18, HolderSnapshot.ShareScale).IsRequired();

        b.HasIndex(r => r.SnapshotId);
    }
}
