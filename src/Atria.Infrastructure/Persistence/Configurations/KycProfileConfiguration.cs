using Atria.Domain.Kyc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class KycProfileConfiguration : IEntityTypeConfiguration<KycProfile>
{
    public void Configure(EntityTypeBuilder<KycProfile> b)
    {
        b.ToTable("kyc_profiles");
        b.HasKey(k => k.Id);

        b.Property(k => k.UserId).IsRequired();
        b.Property(k => k.Status).HasConversion<int>().IsRequired();
        b.Property(k => k.Provider).HasConversion<int>().IsRequired();

        // PII columns: the encrypting converter is applied in OnModelCreating; size left
        // generous to hold ciphertext (base64 of nonce+tag+payload).
        b.Property(k => k.FullName).HasMaxLength(1024);
        b.Property(k => k.DocumentNumber).HasMaxLength(1024);
        b.Property(k => k.Nationality).HasMaxLength(128);
        b.Property(k => k.WalletAddress).HasMaxLength(64);
        b.Property(k => k.ProviderSessionId).HasMaxLength(256);
        b.Property(k => k.VerificationUrl).HasMaxLength(512);
        b.Property(k => k.RejectionReason).HasMaxLength(1024);

        b.HasIndex(k => k.UserId).IsUnique();
        b.HasIndex(k => k.ProviderSessionId);

        // Optimistic concurrency via the Postgres xmin system column.
        b.XminConcurrencyToken();
    }
}
