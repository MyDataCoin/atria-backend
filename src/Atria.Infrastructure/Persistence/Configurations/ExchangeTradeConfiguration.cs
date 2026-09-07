using Atria.Domain.Trading;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class ExchangeTradeConfiguration : IEntityTypeConfiguration<ExchangeTrade>
{
    public void Configure(EntityTypeBuilder<ExchangeTrade> b)
    {
        b.ToTable("exchange_trades");
        b.HasKey(t => t.Id);

        b.Property(t => t.EventId).HasMaxLength(128).IsRequired();
        b.Property(t => t.EventType).HasConversion<int>().IsRequired();
        b.Property(t => t.SequenceNo).IsRequired();
        b.Property(t => t.ExchangeTradeId).HasMaxLength(128).IsRequired();
        b.Property(t => t.PropertyId).IsRequired();
        b.Property(t => t.Chain).HasMaxLength(64).IsRequired();
        b.Property(t => t.TokenContract).HasMaxLength(128).IsRequired();
        b.Property(t => t.TransactionHash).HasMaxLength(128);
        b.Property(t => t.BlockNumber);
        b.Property(t => t.SellerAddress).HasMaxLength(128).IsRequired();
        b.Property(t => t.BuyerAddress).HasMaxLength(128).IsRequired();
        b.Property(t => t.TokenCount).IsRequired();
        b.Property(t => t.PricePerToken).HasPrecision(18, 2).IsRequired();
        b.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        b.Property(t => t.TradedAtUtc).IsRequired();
        b.Property(t => t.SettledAtUtc).IsRequired();
        b.Property(t => t.ReceivedAtUtc).IsRequired();
        b.Property(t => t.CounterpartyVasp).HasMaxLength(256);
        b.Property(t => t.CustodyAddress).HasMaxLength(128);
        b.Property(t => t.ReversesEventId).HasMaxLength(128);
        b.Property(t => t.IsReversed).IsRequired();
        b.Property(t => t.MatchState).HasConversion<int>().IsRequired();
        b.Property(t => t.MatchDetail).HasMaxLength(1024);
        b.Property(t => t.MatchedAtUtc);

        // The counterparty's name is personal data supplied for the travel rule, so it is sized for
        // the encrypting converter exactly as the other identity columns are.
        b.Property(t => t.BeneficiaryName).HasMaxLength(1024);

        // The operator's event id is the idempotency key, enforced by the database rather than by a
        // prior read: two deliveries racing each other must not both insert.
        b.HasIndex(t => t.EventId).IsUnique();

        // The join to what we read from the chain.
        b.HasIndex(t => t.TransactionHash);

        // Sequence-gap detection and the per-issue trade list.
        b.HasIndex(t => new { t.PropertyId, t.SequenceNo });
        b.HasIndex(t => new { t.PropertyId, t.MatchState });

        b.Ignore(t => t.TotalValue);

        b.XminConcurrencyToken();
    }
}
