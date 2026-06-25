using Atria.Domain.Notifications;

namespace Atria.Application.Abstractions;

/// <summary>Low-level email adapter (external provider behind it).</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string body, CancellationToken ct);
}

/// <summary>Low-level SMS adapter. Nikita Pro implements this; OTP and notifications use it.</summary>
public interface ISmsSender
{
    Task SendAsync(string phoneNumber, string message, CancellationToken ct);
}

/// <summary>
/// High-level notification orchestrator: persists a <c>Notification</c>, renders the
/// template, and dispatches over the right channel using the adapters above.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(
        Guid userId,
        NotificationTemplate template,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken ct);
}
