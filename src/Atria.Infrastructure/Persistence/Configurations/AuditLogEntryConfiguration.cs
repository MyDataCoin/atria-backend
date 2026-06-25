using Atria.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> b)
    {
        b.ToTable("audit_log");
        b.HasKey(a => a.Id);

        b.Property(a => a.EntityType).HasMaxLength(256).IsRequired();
        b.Property(a => a.EntityId);
        b.Property(a => a.EventType).HasMaxLength(256).IsRequired();
        b.Property(a => a.DataJson);
        b.Property(a => a.UserId);
        b.Property(a => a.CorrelationId).HasMaxLength(128);
        b.Property(a => a.OccurredOnUtc).IsRequired();

        b.HasIndex(a => a.EntityType);
        b.HasIndex(a => a.EntityId);
    }
}
