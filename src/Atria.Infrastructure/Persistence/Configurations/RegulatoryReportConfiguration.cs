using Atria.Domain.Regulatory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class RegulatoryReportConfiguration : IEntityTypeConfiguration<RegulatoryReport>
{
    public void Configure(EntityTypeBuilder<RegulatoryReport> b)
    {
        b.ToTable("regulatory_reports");
        b.HasKey(r => r.Id);

        b.Property(r => r.Kind).HasConversion<int>().IsRequired();
        b.Property(r => r.PropertyId);
        b.Property(r => r.PeriodStartUtc).IsRequired();
        b.Property(r => r.PeriodEndUtc).IsRequired();
        b.Property(r => r.DueAtUtc).IsRequired();
        b.Property(r => r.Status).HasConversion<int>().IsRequired();
        b.Property(r => r.Content);
        b.Property(r => r.GeneratedAtUtc);
        b.Property(r => r.HolderSnapshotId);
        b.Property(r => r.FiledAtUtc);
        b.Property(r => r.FilingReference).HasMaxLength(128);

        // One obligation per (kind, issue, period): the periodic sweep must not raise a second
        // deadline for a month it has already recorded.
        b.HasIndex(r => new { r.Kind, r.PropertyId, r.PeriodEndUtc }).IsUnique();

        // The reminder queue reads by deadline.
        b.HasIndex(r => new { r.Status, r.DueAtUtc });

        b.XminConcurrencyToken();
    }
}
