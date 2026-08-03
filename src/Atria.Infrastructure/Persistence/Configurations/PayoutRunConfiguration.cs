using Atria.Domain.Payouts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PayoutRunConfiguration : IEntityTypeConfiguration<PayoutRun>
{
    public void Configure(EntityTypeBuilder<PayoutRun> b)
    {
        b.ToTable("payout_runs");
        b.HasKey(r => r.Id);

        b.Property(r => r.PropertyId).IsRequired();
        b.Property(r => r.SnapshotId).IsRequired();
        b.Property(r => r.SnapshotAtUtc).IsRequired();
        b.Property(r => r.Kind).HasConversion<int>().IsRequired();
        b.Property(r => r.Method).HasConversion<int>().IsRequired();
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.DeclaredAmount).HasPrecision(18, 2).IsRequired();
        b.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        b.Property(r => r.TotalTokens).IsRequired();
        b.Property(r => r.CreatedByUserId).IsRequired();
        b.Property(r => r.Note).HasMaxLength(512);
        b.Property(r => r.CancellationReason).HasMaxLength(512);

        // One distribution per cut, enforced in the database: two runs on the same snapshot would
        // pay the same register twice, and the handler's check alone loses a race.
        b.HasIndex(r => r.SnapshotId).IsUnique();
        b.HasIndex(r => r.PropertyId);

        b.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.PayoutRunId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(r => r.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.XminConcurrencyToken();
    }
}

internal sealed class PayoutItemConfiguration : IEntityTypeConfiguration<PayoutItem>
{
    public void Configure(EntityTypeBuilder<PayoutItem> b)
    {
        b.ToTable("payout_items");
        b.HasKey(i => i.Id);

        b.Property(i => i.PayoutRunId).IsRequired();
        b.Property(i => i.InvestorId).IsRequired();
        b.Property(i => i.WalletAddress).HasMaxLength(128).IsRequired();
        b.Property(i => i.TokenCount).IsRequired();
        b.Property(i => i.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(i => i.Status).HasConversion<int>().IsRequired();
        b.Property(i => i.SettlementReference).HasMaxLength(256);
        b.Property(i => i.PaidAtUtc);
        b.Property(i => i.FailureReason).HasMaxLength(512);

        b.HasIndex(i => i.PayoutRunId);
        b.HasIndex(i => i.InvestorId);
    }
}
