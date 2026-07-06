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
        b.Property(u => u.Role).HasConversion<int>().IsRequired();
        b.Property(u => u.IsActive).IsRequired();
        b.Property(u => u.IsPhoneVerified).IsRequired();

        b.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter(null);
    }
}
