using Atria.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PaymentSettingsConfiguration : IEntityTypeConfiguration<PaymentSettings>
{
    public void Configure(EntityTypeBuilder<PaymentSettings> b)
    {
        b.ToTable("payment_settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.UsdtRateKgs).HasPrecision(18, 4);
        b.Property(s => s.Bep20Address).HasMaxLength(PaymentSettings.MaxAddressLength);
        b.Property(s => s.Trc20Address).HasMaxLength(PaymentSettings.MaxAddressLength);
        b.Property(s => s.KgstContractAddress).HasMaxLength(PaymentSettings.MaxAddressLength);
        b.XminConcurrencyToken();
    }
}

internal sealed class CryptoInvoiceConfiguration : IEntityTypeConfiguration<CryptoInvoice>
{
    public void Configure(EntityTypeBuilder<CryptoInvoice> b)
    {
        b.ToTable("crypto_invoices");
        b.HasKey(i => i.Id);
        b.Property(i => i.Method).HasConversion<int>();
        b.Property(i => i.Status).HasConversion<int>();
        b.Property(i => i.PayToAddress).HasMaxLength(PaymentSettings.MaxAddressLength).IsRequired();
        b.Property(i => i.PayAmount).HasPrecision(28, 8);
        b.Property(i => i.ReceivedAmount).HasPrecision(28, 8);
        b.Ignore(i => i.ShortfallKgs);
        b.Property(i => i.RateKgs).HasPrecision(18, 4);
        b.Property(i => i.AmountKgs).HasPrecision(18, 2);
        b.Property(i => i.TransactionHash).HasMaxLength(CryptoDeposit.MaxKeyLength);
        b.Property(i => i.FromAddress).HasMaxLength(PaymentSettings.MaxAddressLength);

        b.HasIndex(i => i.InvestmentId);
        b.HasIndex(i => new { i.Method, i.Status, i.PayAmount });

        // The amount is the only thing tying an anonymous transfer to an application, so two pending
        // invoices of one method may never ask for the same amount — enforced here, not just hoped for.
        b.HasIndex(i => new { i.Method, i.PayAmount })
            .IsUnique()
            .HasFilter("\"Status\" = 0")
            .HasDatabaseName("ux_crypto_invoices_pending_amount");

        b.XminConcurrencyToken();
    }
}

internal sealed class CryptoDepositConfiguration : IEntityTypeConfiguration<CryptoDeposit>
{
    public void Configure(EntityTypeBuilder<CryptoDeposit> b)
    {
        b.ToTable("crypto_deposits");
        b.HasKey(d => d.Id);
        b.Property(d => d.Method).HasConversion<int>();
        b.Property(d => d.TransferKey).HasMaxLength(CryptoDeposit.MaxKeyLength).IsRequired();
        b.Property(d => d.TransactionHash).HasMaxLength(CryptoDeposit.MaxKeyLength).IsRequired();
        b.Property(d => d.FromAddress).HasMaxLength(PaymentSettings.MaxAddressLength).IsRequired();
        b.Property(d => d.ToAddress).HasMaxLength(PaymentSettings.MaxAddressLength).IsRequired();
        b.Property(d => d.Amount).HasPrecision(28, 8);

        b.HasIndex(d => new { d.Method, d.TransferKey }).IsUnique();
        b.HasIndex(d => d.InvoiceId);
        b.HasIndex(d => d.OccurredAtUtc);
    }
}

internal sealed class CryptoWatchCursorConfiguration : IEntityTypeConfiguration<CryptoWatchCursor>
{
    public void Configure(EntityTypeBuilder<CryptoWatchCursor> b)
    {
        b.ToTable("crypto_watch_cursors");
        b.HasKey(c => c.Id);
        b.Property(c => c.Key).HasMaxLength(CryptoWatchCursor.MaxKeyLength).IsRequired();
        b.HasIndex(c => c.Key).IsUnique();
    }
}
