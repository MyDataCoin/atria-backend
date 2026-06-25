using Atria.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atria.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.HasKey(n => n.Id);

        b.Property(n => n.UserId).IsRequired();
        b.Property(n => n.Template).HasConversion<int>().IsRequired();
        b.Property(n => n.Channel).HasConversion<int>().IsRequired();
        b.Property(n => n.Title).HasMaxLength(256).IsRequired();
        b.Property(n => n.Body).HasMaxLength(4096).IsRequired();
        b.Property(n => n.IsRead).IsRequired();

        b.HasIndex(n => n.UserId);
    }
}
