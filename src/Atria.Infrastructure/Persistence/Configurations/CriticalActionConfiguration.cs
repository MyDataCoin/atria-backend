using Atria.Domain.Governance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class CriticalActionConfiguration : IEntityTypeConfiguration<CriticalAction>
{
    public void Configure(EntityTypeBuilder<CriticalAction> b)
    {
        b.ToTable("critical_actions");
        b.HasKey(a => a.Id);

        b.Property(a => a.Kind).HasConversion<int>().IsRequired();
        b.Property(a => a.TargetId).IsRequired();
        b.Property(a => a.Reason).HasMaxLength(512);
        b.Property(a => a.RequestedByUserId).IsRequired();
        b.Property(a => a.RequestedAtUtc).IsRequired();
        b.Property(a => a.ExpiresAtUtc).IsRequired();
        b.Property(a => a.Status).HasConversion<int>().IsRequired();
        b.Property(a => a.DecidedByUserId);
        b.Property(a => a.DecidedAtUtc);
        b.Property(a => a.DecisionNote).HasMaxLength(512);

        // The pending queue and the "is there already a request for this?" lookup.
        b.HasIndex(a => new { a.Status, a.Kind, a.TargetId });

        b.XminConcurrencyToken();
    }
}
