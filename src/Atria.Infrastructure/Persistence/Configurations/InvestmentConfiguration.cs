using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class InvestmentConfiguration : IEntityTypeConfiguration<Investment>
{
    public void Configure(EntityTypeBuilder<Investment> b)
    {
        b.ToTable("investments");
        b.HasKey(i => i.Id);

        b.Property(i => i.InvestorId).IsRequired();
        b.Property(i => i.PropertyId).IsRequired();
        b.Property(i => i.ApplicationId).IsRequired();
        b.Property(i => i.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(i => i.Currency).HasMaxLength(8).IsRequired();
        b.Property(i => i.Status).HasConversion<int>().IsRequired();

        b.HasIndex(i => i.InvestorId);
        b.HasIndex(i => i.ApplicationId);

        // Map the PaymentTransaction child collection via the backing field as a
        // separate FK table owned by the Investment aggregate.
        b.HasMany(i => i.Payments)
            .WithOne()
            .HasForeignKey(p => p.InvestmentId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Payments)
            .HasField("_payments")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.XminConcurrencyToken();
    }
}
