using Atria.Domain.Holders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class ChainSyncCursorConfiguration : IEntityTypeConfiguration<ChainSyncCursor>
{
    public void Configure(EntityTypeBuilder<ChainSyncCursor> b)
    {
        b.ToTable("chain_sync_cursors");
        b.HasKey(c => c.Id);

        b.Property(c => c.PropertyId).IsRequired();
        b.Property(c => c.LastProcessedBlock).IsRequired();
        b.Property(c => c.LastSyncedAtUtc).IsRequired();

        // One cursor per issue: a second would replay the same blocks and double-count every transfer.
        b.HasIndex(c => c.PropertyId).IsUnique();

        b.XminConcurrencyToken();
    }
}
