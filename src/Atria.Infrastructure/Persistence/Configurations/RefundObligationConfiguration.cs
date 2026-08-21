using Atria.Domain.Refunds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class RefundObligationConfiguration : IEntityTypeConfiguration<RefundObligation>
{
    public void Configure(EntityTypeBuilder<RefundObligation> b)
    {
        b.ToTable("refund_obligations");
        b.HasKey(r => r.Id);

        b.Property(r => r.PropertyId).IsRequired();
        b.Property(r => r.InvestorId).IsRequired();
        b.Property(r => r.WalletAddress).HasMaxLength(128).IsRequired();
        b.Property(r => r.TokenCount).IsRequired();
        b.Property(r => r.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(r => r.Currency).HasMaxLength(8).IsRequired();
        b.Property(r => r.Reason).HasConversion<int>().IsRequired();
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.Note).HasMaxLength(512);
        b.Property(r => r.SettledAtUtc);
        b.Property(r => r.SettlementReference).HasMaxLength(128);

        // One obligation per (issue, address, reason): re-running an invalidation must not owe the
        // same holder twice.
        b.HasIndex(r => new { r.PropertyId, r.WalletAddress, r.Reason }).IsUnique();

        // The settlement queue.
        b.HasIndex(r => new { r.Status, r.CreatedAtUtc });

        b.XminConcurrencyToken();
    }
}
