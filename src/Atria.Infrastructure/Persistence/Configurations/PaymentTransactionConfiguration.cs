using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PaymentTransactionConfiguration : IEntityTypeConfiguration<PaymentTransaction>
{
    public void Configure(EntityTypeBuilder<PaymentTransaction> b)
    {
        b.ToTable("payment_transactions");
        b.HasKey(p => p.Id);

        b.Property(p => p.InvestmentId).IsRequired();
        b.Property(p => p.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.Currency).HasMaxLength(8).IsRequired();
        b.Property(p => p.Provider).HasConversion<int>().IsRequired();
        b.Property(p => p.Status).HasConversion<int>().IsRequired();
        b.Property(p => p.ExternalPaymentId).HasMaxLength(256);
        b.Property(p => p.FailureReason).HasMaxLength(1024);

        b.HasIndex(p => p.InvestmentId);
        b.HasIndex(p => p.ExternalPaymentId);
    }
}
