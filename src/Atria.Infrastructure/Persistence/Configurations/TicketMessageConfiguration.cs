using Atria.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> b)
    {
        b.ToTable("ticket_messages");
        b.HasKey(m => m.Id);

        // Id is assigned in TicketMessage.Create (Guid.NewGuid()), never by EF/the database. Without
        // ValueGeneratedNever, EF's Guid-key convention (ValueGeneratedOnAdd) sees an already-set key
        // on a child discovered via SupportTicket._messages fixup and tracks it as Unchanged/Modified
        // instead of Added -> the INSERT becomes a 0-row UPDATE (DbUpdateConcurrencyException).
        b.Property(m => m.Id).ValueGeneratedNever();

        b.Property(m => m.TicketId).IsRequired();
        b.Property(m => m.Author).HasConversion<int>().IsRequired();
        b.Property(m => m.Body).HasMaxLength(8000).IsRequired();

        b.HasIndex(m => m.TicketId);
    }
}
