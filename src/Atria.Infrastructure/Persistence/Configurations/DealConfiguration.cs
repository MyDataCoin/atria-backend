using Atria.Domain.Deals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class DealConfiguration : IEntityTypeConfiguration<Deal>
{
    public void Configure(EntityTypeBuilder<Deal> b)
    {
        b.ToTable("deals");
        b.HasKey(d => d.Id);

        b.Property(d => d.RealtorId).IsRequired();
        b.Property(d => d.PropertyId).IsRequired();
        b.Property(d => d.CommissionPercent).HasPrecision(5, 2).IsRequired();
        b.Property(d => d.ReferralToken).HasMaxLength(64).IsRequired();
        b.Property(d => d.Status).HasConversion<int>().IsRequired();
        b.Property(d => d.ExpiresAtUtc).IsRequired();
        b.Property(d => d.MatchedInvestmentId);

        b.HasIndex(d => d.ReferralToken).IsUnique();
        b.HasIndex(d => d.RealtorId);
        // Supports the expiry sweep: pending deals ordered by expiry.
        b.HasIndex(d => new { d.Status, d.ExpiresAtUtc });

        b.XminConcurrencyToken();
    }
}
