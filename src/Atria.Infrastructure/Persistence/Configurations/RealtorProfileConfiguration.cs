using Atria.Domain.Realtors;
using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class RealtorProfileConfiguration : IEntityTypeConfiguration<RealtorProfile>
{
    public void Configure(EntityTypeBuilder<RealtorProfile> b)
    {
        b.ToTable("realtor_profiles");
        b.HasKey(p => p.Id);

        b.Property(p => p.UserId).IsRequired();
        b.Property(p => p.FullName).HasMaxLength(256).IsRequired();
        b.Property(p => p.Position).HasMaxLength(128);
        b.Property(p => p.WalletAddress).HasMaxLength(128);
        b.Property(p => p.CompanyName).HasMaxLength(256);
        b.Property(p => p.CompanyRegistrationNumber).HasMaxLength(64);
        b.Property(p => p.OfficeAddress).HasMaxLength(512);

        // One profile per user, enforced by a hard FK to users (a profile cannot exist without
        // its user row). Cascade so deleting the user removes the profile.
        b.HasIndex(p => p.UserId).IsUnique();
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.XminConcurrencyToken();
    }
}
