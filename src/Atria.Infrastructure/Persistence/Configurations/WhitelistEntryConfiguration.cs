using Atria.Domain.Whitelist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class WhitelistEntryConfiguration : IEntityTypeConfiguration<WhitelistEntry>
{
    public void Configure(EntityTypeBuilder<WhitelistEntry> b)
    {
        b.ToTable("whitelist_entries");
        b.HasKey(e => e.Id);

        b.Property(e => e.InvestmentId).IsRequired();
        b.Property(e => e.InvestorId).IsRequired();
        b.Property(e => e.PropertyId).IsRequired();
        b.Property(e => e.TokenCount).HasPrecision(28, 2).IsRequired();
        b.Property(e => e.WalletAddress).HasMaxLength(128);
        b.Property(e => e.Status).HasConversion<int>().IsRequired();
        b.Property(e => e.RequestedAtUtc).IsRequired();
        b.Property(e => e.ApprovedAtUtc);
        b.Property(e => e.MintListId);
        b.Property(e => e.MintedAtUtc);
        b.Property(e => e.ExclusionReason).HasMaxLength(512);

        // One request per purchase, enforced in the database: the queue is fed by a redeliverable
        // domain event, and the handler's own guard is not what a duplicate must run into last.
        b.HasIndex(e => e.InvestmentId).IsUnique();

        // The two reads the queue actually performs: an issuance's requests by status, and everything
        // a batch is holding.
        b.HasIndex(e => new { e.PropertyId, e.Status });
        b.HasIndex(e => e.MintListId);

        b.XminConcurrencyToken();
    }
}
