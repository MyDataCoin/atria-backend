using Atria.Domain.Appeals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class AppealConfiguration : IEntityTypeConfiguration<Appeal>
{
    public void Configure(EntityTypeBuilder<Appeal> b)
    {
        b.ToTable("appeals");
        b.HasKey(a => a.Id);

        // Username is a soft reference (the account may not exist / may be spelled wrong), so no FK.
        b.Property(a => a.Username).HasMaxLength(64).IsRequired();
        b.Property(a => a.Message).HasMaxLength(Appeal.MaxMessageLength).IsRequired();

        b.HasIndex(a => a.Username);
        b.HasIndex(a => a.CreatedAtUtc);

        b.XminConcurrencyToken();
    }
}
