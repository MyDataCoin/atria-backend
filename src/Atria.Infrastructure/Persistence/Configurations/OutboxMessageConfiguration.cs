using Atria.Domain.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> b)
    {
        b.ToTable("outbox_messages");
        b.HasKey(m => m.Id);

        b.Property(m => m.EventId).IsRequired();
        b.Property(m => m.Type).IsRequired();
        b.Property(m => m.Payload).IsRequired();
        b.Property(m => m.OccurredOnUtc).IsRequired();
        b.Property(m => m.ProcessedOnUtc);
        b.Property(m => m.Attempts).IsRequired();
        b.Property(m => m.Error).HasMaxLength(2048);

        // The dispatcher polls unprocessed rows in occurrence order.
        b.HasIndex(m => m.ProcessedOnUtc);
        b.HasIndex(m => m.EventId);
    }
}
