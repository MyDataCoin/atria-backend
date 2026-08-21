using Atria.Domain.Whitelist;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class MintListConfiguration : IEntityTypeConfiguration<MintList>
{
    public void Configure(EntityTypeBuilder<MintList> b)
    {
        b.ToTable("mint_lists");
        b.HasKey(l => l.Id);

        b.Property(l => l.Number).HasMaxLength(32).IsRequired();
        b.Property(l => l.PropertyId).IsRequired();
        b.Property(l => l.TokenContractAddress).HasMaxLength(128);
        b.Property(l => l.TokenChain).HasMaxLength(64);
        b.Property(l => l.Status).HasConversion<int>().IsRequired();
        b.Property(l => l.TotalTokens).IsRequired();
        b.Property(l => l.ItemCount).IsRequired();
        b.Property(l => l.CreatedByUserId).IsRequired();
        b.Property(l => l.SentAtUtc);
        b.Property(l => l.ExecutedAtUtc);
        b.Property(l => l.Note).HasMaxLength(1024);
        b.Property(l => l.CancellationReason).HasMaxLength(512);

        // The batch number is what the exchange quotes back, so two batches can never share one.
        // This index is also what settles the race between two operators assembling at the same instant.
        b.HasIndex(l => l.Number).IsUnique();
        b.HasIndex(l => l.PropertyId);

        // Lines owned by the batch aggregate, mapped via the backing field.
        b.HasMany(l => l.Items)
            .WithOne()
            .HasForeignKey(i => i.MintListId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(l => l.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        b.XminConcurrencyToken();
    }
}

internal sealed class MintListItemConfiguration : IEntityTypeConfiguration<MintListItem>
{
    public void Configure(EntityTypeBuilder<MintListItem> b)
    {
        b.ToTable("mint_list_items");
        b.HasKey(i => i.Id);

        b.Property(i => i.MintListId).IsRequired();
        b.Property(i => i.WhitelistEntryId).IsRequired();
        b.Property(i => i.InvestmentId).IsRequired();
        b.Property(i => i.InvestorId).IsRequired();
        b.Property(i => i.WalletAddress).HasMaxLength(128).IsRequired();
        b.Property(i => i.TokenCount).IsRequired();

        b.HasIndex(i => i.MintListId);

        // A request appears in at most one batch line, ever. Releasing a request from a cancelled batch
        // leaves that batch's line in place — it is the record of what was handed over — so the guard
        // is per (batch, request) rather than per request.
        b.HasIndex(i => new { i.MintListId, i.WhitelistEntryId }).IsUnique();
    }
}
