using Atria.Domain.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("support_tickets");
        b.HasKey(t => t.Id);

        b.Property(t => t.InvestorId).IsRequired();
        b.Property(t => t.Subject).HasMaxLength(SupportTicket.MaxSubjectLength).IsRequired();
        b.Property(t => t.Category).HasMaxLength(64).IsRequired();
        b.Property(t => t.Status).HasConversion<int>().IsRequired();

        // Message thread mapped via the backing field, owned by the ticket aggregate.
        b.HasMany(t => t.Messages)
            .WithOne()
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(t => t.Messages)
            .HasField("_messages")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(t => t.InvestorId);
        b.HasIndex(t => t.Status);
    }
}
