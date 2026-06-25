using Atria.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> b)
    {
        b.ToTable("properties");
        b.HasKey(p => p.Id);

        b.Property(p => p.Name).HasMaxLength(256).IsRequired();
        b.Property(p => p.Description).HasMaxLength(4096);
        b.Property(p => p.Address).HasMaxLength(512);
        b.Property(p => p.TotalValue).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.TokenPrice).HasPrecision(18, 2).IsRequired();
        b.Property(p => p.TotalTokens).IsRequired();
        b.Property(p => p.AvailableTokens).IsRequired();
        b.Property(p => p.Currency).HasMaxLength(8).IsRequired();
        b.Property(p => p.IsActive).IsRequired();
    }
}
