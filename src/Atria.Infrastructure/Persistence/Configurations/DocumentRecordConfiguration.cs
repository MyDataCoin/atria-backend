using Atria.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class DocumentRecordConfiguration : IEntityTypeConfiguration<DocumentRecord>
{
    public void Configure(EntityTypeBuilder<DocumentRecord> b)
    {
        b.ToTable("documents");
        b.HasKey(d => d.Id);

        b.Property(d => d.OwnerUserId).IsRequired();
        b.Property(d => d.Type).HasConversion<int>().IsRequired();
        b.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(256).IsRequired();
        b.Property(d => d.StorageKey).HasMaxLength(1024).IsRequired();
        b.Property(d => d.SizeBytes).IsRequired();

        b.HasIndex(d => d.OwnerUserId);
    }
}
