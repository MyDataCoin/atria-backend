using Atria.Domain.TravelRule;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class TravelRuleMessageConfiguration : IEntityTypeConfiguration<TravelRuleMessage>
{
    public void Configure(EntityTypeBuilder<TravelRuleMessage> b)
    {
        b.ToTable("travel_rule_messages");
        b.HasKey(m => m.Id);

        b.Property(m => m.PropertyId).IsRequired();
        b.Property(m => m.InvestorId).IsRequired();
        b.Property(m => m.Direction).HasConversion<int>().IsRequired();
        b.Property(m => m.Status).HasConversion<int>().IsRequired();
        b.Property(m => m.TokenCount).HasPrecision(28, 2).IsRequired();
        b.Property(m => m.Amount).HasPrecision(18, 2).IsRequired();
        b.Property(m => m.Currency).HasMaxLength(3).IsRequired();
        b.Property(m => m.OriginatorAddress).HasMaxLength(128).IsRequired();
        b.Property(m => m.BeneficiaryAddress).HasMaxLength(128).IsRequired();
        b.Property(m => m.CounterpartyVasp).HasMaxLength(256);
        b.Property(m => m.TransactionHash).HasMaxLength(128);
        b.Property(m => m.CounterpartyReference).HasMaxLength(256);
        b.Property(m => m.FailureReason).HasMaxLength(512);
        b.Property(m => m.SentAtUtc);

        // Personal data, and the payload that carries it. Wide enough for the ciphertext the
        // encrypting converter produces, exactly as the KYC columns are.
        b.Property(m => m.OriginatorName).HasMaxLength(1024).IsRequired();
        b.Property(m => m.OriginatorDocumentNumber).HasMaxLength(1024);
        b.Property(m => m.OriginatorNationality).HasMaxLength(64);
        b.Property(m => m.BeneficiaryName).HasMaxLength(1024);
        b.Property(m => m.Payload).HasMaxLength(8192);

        // One disclosure per transfer. A replayed block must not produce a second.
        b.HasIndex(m => new { m.TransactionHash, m.OriginatorAddress, m.BeneficiaryAddress });
        b.HasIndex(m => m.Status);
        b.HasIndex(m => m.PropertyId);

        b.XminConcurrencyToken();
    }
}
