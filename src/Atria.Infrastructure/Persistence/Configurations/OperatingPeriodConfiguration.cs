using Atria.Domain.Operations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class OperatingPeriodConfiguration : IEntityTypeConfiguration<OperatingPeriod>
{
    public void Configure(EntityTypeBuilder<OperatingPeriod> b)
    {
        b.ToTable("operating_periods");
        b.HasKey(p => p.Id);

        b.Property(p => p.PropertyId).IsRequired();
        b.Property(p => p.StartUtc).IsRequired();
        b.Property(p => p.EndUtc).IsRequired();

        b.Property(p => p.GrossRevenue).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.OperatingExpenses).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.Currency).HasMaxLength(8).IsRequired();

        b.Property(p => p.Status).HasConversion<int>().IsRequired();
        b.Property(p => p.ReportedByUserId).IsRequired();
        b.Property(p => p.ConfirmedByUserId);
        b.Property(p => p.ConfirmedAtUtc);
        b.Property(p => p.PayoutRunId);
        b.Property(p => p.Note).HasMaxLength(2048);

        // One period per issue per stretch of time. Reporting the same month twice would let the same
        // income be confirmed and distributed twice over, which is the failure this whole aggregate
        // exists to prevent — so the database refuses it rather than trusting every caller to check.
        b.HasIndex(p => new { p.PropertyId, p.StartUtc, p.EndUtc }).IsUnique();

        // The listing read: an issue's periods, newest first.
        b.HasIndex(p => new { p.PropertyId, p.EndUtc });

        b.HasOne<Domain.Investments.Property>()
            .WithMany()
            .HasForeignKey(p => p.PropertyId)
            .OnDelete(DeleteBehavior.Restrict);

        b.XminConcurrencyToken();

        // ReplaceLines() clears and refills the collection, so the required FK makes EF delete the
        // dropped rows as orphans instead of orphaning them.
        b.HasMany(p => p.Lines)
            .WithOne()
            .HasForeignKey(l => l.OperatingPeriodId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.Lines)
            .HasField("_lines")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OperatingPeriodLineConfiguration : IEntityTypeConfiguration<OperatingPeriodLine>
{
    public void Configure(EntityTypeBuilder<OperatingPeriodLine> b)
    {
        b.ToTable("operating_period_lines");
        b.HasKey(l => l.Id);

        b.Property(l => l.OperatingPeriodId).IsRequired();
        b.Property(l => l.Kind).HasConversion<int>().IsRequired();
        b.Property(l => l.Label).HasMaxLength(OperatingPeriod.MaxLineLabel).IsRequired();
        b.Property(l => l.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(l => l.SortOrder).IsRequired();

        b.HasIndex(l => l.OperatingPeriodId);
    }
}
