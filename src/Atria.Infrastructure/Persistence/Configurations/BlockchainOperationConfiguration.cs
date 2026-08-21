using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class BlockchainOperationConfiguration : IEntityTypeConfiguration<BlockchainOperation>
{
    public void Configure(EntityTypeBuilder<BlockchainOperation> b)
    {
        b.ToTable("blockchain_operations");
        b.HasKey(o => o.Id);

        b.Property(o => o.Type).HasConversion<int>().IsRequired();
        b.Property(o => o.Payload).IsRequired();
        b.Property(o => o.IdempotencyKey).HasMaxLength(256).IsRequired();
        b.Property(o => o.Status).HasConversion<int>().IsRequired();
        b.Property(o => o.Attempts).IsRequired();
        b.Property(o => o.TransactionRef).HasMaxLength(256);
        b.Property(o => o.Error).HasMaxLength(2048);
        b.Property(o => o.ConfirmedAtUtc);
        b.Property(o => o.Confirmations);

        b.HasIndex(o => o.IdempotencyKey).IsUnique();
        b.HasIndex(o => o.Status);
        // The queue screen lists newest first and filters by status; without this the operator's
        // default view is a sequential scan that grows with every operation ever run.
        b.HasIndex(o => new { o.Status, o.CreatedAtUtc });
    }
}
