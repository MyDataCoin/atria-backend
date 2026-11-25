using Atria.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class TelegramLinkConfiguration : IEntityTypeConfiguration<TelegramLink>
{
    public void Configure(EntityTypeBuilder<TelegramLink> b)
    {
        b.ToTable("telegram_links");
        b.HasKey(l => l.Id);
        b.Property(l => l.Phone).HasMaxLength(TelegramLink.MaxPhoneLength).IsRequired();
        b.HasIndex(l => l.Phone).IsUnique();
        b.HasIndex(l => l.TelegramUserId);
    }
}
