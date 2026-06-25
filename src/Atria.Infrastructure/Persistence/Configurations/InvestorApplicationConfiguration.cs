using Atria.Domain.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class InvestorApplicationConfiguration : IEntityTypeConfiguration<InvestorApplication>
{
    public void Configure(EntityTypeBuilder<InvestorApplication> b)
    {
        b.ToTable("investor_applications");
        b.HasKey(a => a.Id);

        b.Property(a => a.InvestorId).IsRequired();
        b.Property(a => a.PropertyId).IsRequired();
        b.Property(a => a.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(a => a.Status).HasConversion<int>().IsRequired();
        b.Property(a => a.RejectionReason).HasMaxLength(1024);

        b.HasIndex(a => a.InvestorId);
        b.HasIndex(a => a.PropertyId);

        b.XminConcurrencyToken();
    }
}
