using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> b)
    {
        b.ToTable("otp_codes");
        b.HasKey(o => o.Id);

        b.Property(o => o.Phone).HasMaxLength(32).IsRequired();
        b.Property(o => o.CodeHash).HasMaxLength(128).IsRequired();
        b.Property(o => o.ExpiresAtUtc).IsRequired();
        b.Property(o => o.Attempts).IsRequired();
        b.Property(o => o.Consumed).IsRequired();
        b.Property(o => o.CreatedAtUtc).IsRequired();

        b.HasIndex(o => o.Phone);
    }
}
