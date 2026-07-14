using Atria.Domain.Common;

namespace Atria.Domain.Notifications;

/// <summary>
/// An outgoing notification (email / SMS / push) delivered to a user.
/// Tracks read state. Raises no domain events.
/// </summary>
public sealed class Notification : AggregateRoot
{
    public Guid UserId { get; private set; }
    public NotificationTemplate Template { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public string Title { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public bool IsRead { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>
    /// The entity this notification points at (e.g. the published publication), so tapping it in the
    /// app can deep-link somewhere. Null when the notification has no target.
    /// </summary>
    public Guid? EntityId { get; private set; }

    // private ctor: creation only through the factory method
    private Notification(
        Guid userId,
        NotificationTemplate template,
        NotificationChannel channel,
        string title,
        string body,
        Guid? entityId)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Template = template;
        Channel = channel;
        Title = title;
        Body = body;
        EntityId = entityId;
        IsRead = false;
    }

    public static Notification Create(
        Guid userId,
        NotificationTemplate template,
        NotificationChannel channel,
        string title,
        string body,
        Guid? entityId = null)
        => new(userId, template, channel, title, body, entityId);

    /// <summary>Marks the notification as read at the given UTC instant (idempotent).</summary>
    public void MarkRead(DateTime utc)
    {
        IsRead = true;
        ReadAtUtc = utc;
    }
}
