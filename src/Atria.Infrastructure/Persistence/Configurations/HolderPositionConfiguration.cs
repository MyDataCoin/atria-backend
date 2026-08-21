using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class HolderPositionConfiguration : IEntityTypeConfiguration<HolderPosition>
{
    public void Configure(EntityTypeBuilder<HolderPosition> b)
    {
        b.ToTable("holder_positions");
        b.HasKey(p => p.Id);

        b.Property(p => p.PropertyId).IsRequired();
        b.Property(p => p.WalletAddress).HasMaxLength(128).IsRequired();
        b.Property(p => p.TokenCount).IsRequired();
        b.Property(p => p.InvestorId);
        b.Property(p => p.IsAllowlisted).IsRequired();
        b.Property(p => p.LastSyncedAtUtc).IsRequired();
        b.Property(p => p.Source).HasConversion<int>().IsRequired();

        // One record per wallet per issuance — the natural key of the current-state projection.
        b.HasIndex(p => new { p.PropertyId, p.WalletAddress }).IsUnique();
        b.HasIndex(p => p.InvestorId);

        b.XminConcurrencyToken();
    }
}
