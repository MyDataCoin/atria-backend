using Atria.Domain.Compliance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class ComplianceProfileConfiguration : IEntityTypeConfiguration<ComplianceProfile>
{
    public void Configure(EntityTypeBuilder<ComplianceProfile> b)
    {
        b.ToTable("compliance_profiles");
        b.HasKey(c => c.Id);

        b.Property(c => c.InvestorId).IsRequired();
        b.Property(c => c.Did).HasMaxLength(256);
        b.Property(c => c.WalletAddress).HasMaxLength(64);
        b.Property(c => c.IsAllowlisted).IsRequired();
        b.Property(c => c.IsRevoked).IsRequired();
        b.Property(c => c.AttestationsJson);
        b.Property(c => c.RevocationReason).HasMaxLength(1024);

        b.HasIndex(c => c.InvestorId).IsUnique();
    }
}
