using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(r => r.Id);

        b.Property(r => r.UserId).IsRequired();
        b.Property(r => r.TokenHash).HasMaxLength(128).IsRequired();
        b.Property(r => r.ExpiresAtUtc).IsRequired();
        b.Property(r => r.IsRevoked).IsRequired();
        b.Property(r => r.CreatedAtUtc).IsRequired();

        b.HasIndex(r => r.TokenHash).IsUnique();
        b.HasIndex(r => r.UserId);
    }
}
