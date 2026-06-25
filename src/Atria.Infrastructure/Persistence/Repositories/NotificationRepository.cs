using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Repositories;

public sealed class NotificationRepository : Repository<Notification>, INotificationRepository
{
    public NotificationRepository(AtriaDbContext db) : base(db) { }

    public async Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, CancellationToken ct)
        => await Set.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(ct);
}
