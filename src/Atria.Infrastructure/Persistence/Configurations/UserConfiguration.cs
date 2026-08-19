using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(u => u.Id);

        b.Property(u => u.PhoneNumber).HasMaxLength(32);
        b.Property(u => u.Username).HasMaxLength(64);
        b.Property(u => u.FullName).HasMaxLength(256);
        b.Property(u => u.Role).HasConversion<int>().IsRequired();
        b.Property(u => u.IsActive).IsRequired();
        b.Property(u => u.IsPhoneVerified).IsRequired();
        b.Property(u => u.IsBanned).IsRequired().HasDefaultValue(false);
        b.Property(u => u.BanReason).HasMaxLength(1000);
        b.Property(u => u.PasswordHash).HasMaxLength(200);
        b.Property(u => u.MustResetPassword).IsRequired().HasDefaultValue(false);
        b.Property(u => u.FailedLoginCount).IsRequired().HasDefaultValue(0);
        b.Property(u => u.LockedUntilUtc);
        // Existing rows get a stamp so tokens minted before this column existed do not validate:
        // an unverifiable token must not be a trusted one.
        b.Property(u => u.SecurityStamp)
            .IsRequired()
            .HasMaxLength(64)
            .HasDefaultValueSql("replace(gen_random_uuid()::text, '-', '')");

        b.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter(null);
        // Username is unique among the rows that have one (credential accounts); investors are null.
        b.HasIndex(u => u.Username).IsUnique();
    }
}
