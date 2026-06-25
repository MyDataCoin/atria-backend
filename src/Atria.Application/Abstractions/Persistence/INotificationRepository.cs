using Atria.Domain.Notifications;

namespace Atria.Application.Abstractions;

/// <summary>
/// Specialized repository for <see cref="Notification"/> aggregates.
/// Adds a user-scoped query for the user's notification feed.
/// </summary>
public interface INotificationRepository : IRepository<Notification>
{
    Task<IReadOnlyList<Notification>> GetByUserAsync(Guid userId, CancellationToken ct);
}
