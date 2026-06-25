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

    // private ctor: creation only through the factory method
    private Notification(
        Guid userId,
        NotificationTemplate template,
        NotificationChannel channel,
        string title,
        string body)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        Template = template;
        Channel = channel;
        Title = title;
        Body = body;
        IsRead = false;
    }

    public static Notification Create(
        Guid userId,
        NotificationTemplate template,
        NotificationChannel channel,
        string title,
        string body)
        => new(userId, template, channel, title, body);

    /// <summary>Marks the notification as read at the given UTC instant (idempotent).</summary>
    public void MarkRead(DateTime utc)
    {
        IsRead = true;
        ReadAtUtc = utc;
    }
}
