using Atria.Domain.Tax;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class TaxStatementConfiguration : IEntityTypeConfiguration<TaxStatement>
{
    public void Configure(EntityTypeBuilder<TaxStatement> b)
    {
        b.ToTable("tax_statements");
        b.HasKey(s => s.Id);

        b.Property(s => s.InvestorId).IsRequired();
        b.Property(s => s.Year).IsRequired();
        b.Property(s => s.Number).HasMaxLength(64).IsRequired();
        b.Property(s => s.VerificationCode).HasMaxLength(64).IsRequired();
        b.Property(s => s.InvestorFullName).HasMaxLength(256).IsRequired();
        b.Property(s => s.TotalInvested).HasPrecision(18, 2).IsRequired();
        b.Property(s => s.TotalIncome).HasPrecision(18, 2).IsRequired();
        b.Property(s => s.Currency).HasMaxLength(8).IsRequired();
        b.Property(s => s.Content).IsRequired();
        b.Property(s => s.IssuedAtUtc).IsRequired();

        // One statement per investor and year: a second one for the same period would be a second
        // document with different figures making the same claim.
        b.HasIndex(s => new { s.InvestorId, s.Year }).IsUnique();
        b.HasIndex(s => s.VerificationCode).IsUnique();
    }
}
