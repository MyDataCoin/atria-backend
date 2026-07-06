using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Atria.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Notifications;

/// <summary>
/// High-level <see cref="INotificationSender"/>. Renders a (title, body) for the
/// template, persists a <see cref="Notification"/>, then delivers it over the channel
/// selected by the template (OtpCode -> SMS, everything else -> Email) using the
/// low-level <see cref="ISmsSender"/> / <see cref="IEmailSender"/> adapters.
/// </summary>
public sealed class NotificationSender : INotificationSender
{
    private readonly INotificationRepository _notifications;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISmsSender _smsSender;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationSender> _logger;

    public NotificationSender(
        INotificationRepository notifications,
        IUserRepository users,
        IUnitOfWork unitOfWork,
        ISmsSender smsSender,
        IEmailSender emailSender,
        ILogger<NotificationSender> logger)
    {
        _notifications = notifications;
        _users = users;
        _unitOfWork = unitOfWork;
        _smsSender = smsSender;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task SendAsync(
        Guid userId,
        NotificationTemplate template,
        IReadOnlyDictionary<string, string>? data,
        CancellationToken ct)
    {
        var user = await _users.GetByIdAsync(userId, ct);
        if (user is null)
        {
            _logger.LogWarning(
                "Notification skipped: user not found. UserId={UserId} Template={Template}",
                userId, template);
            return;
        }

        var channel = ChannelFor(template);
        var (title, body) = Render(template, data);

        // Persist the in-app record first (transactional outbox handles durability).
        var notification = Notification.Create(userId, template, channel, title, body);
        await _notifications.AddAsync(notification, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        // Then dispatch over the chosen channel using the user's contact details.
        await DispatchAsync(channel, user, title, body, template, ct);
    }

    // Template -> channel routing (no if/else over a status: a simple switch map).
    private static NotificationChannel ChannelFor(NotificationTemplate template) =>
        template switch
        {
            NotificationTemplate.OtpCode => NotificationChannel.Sms,
            _ => NotificationChannel.Email
        };

    private async Task DispatchAsync(
        NotificationChannel channel,
        User user,
        string title,
        string body,
        NotificationTemplate template,
        CancellationToken ct)
    {
        switch (channel)
        {
            case NotificationChannel.Sms when !string.IsNullOrWhiteSpace(user.PhoneNumber):
                await _smsSender.SendAsync(user.PhoneNumber!, body, ct);
                break;

            default:
                // Persisted in-app only. Accounts are phone-only, so non-SMS channels
                // (e.g. Email) have no delivery target and are surfaced in-app.
                _logger.LogWarning(
                    "Notification persisted but not delivered: missing contact. UserId={UserId} Channel={Channel} Template={Template}",
                    user.Id, channel, template);
                break;
        }
    }

    // Renders title + body from the template and (optional) substitution data.
    private static (string Title, string Body) Render(
        NotificationTemplate template,
        IReadOnlyDictionary<string, string>? data)
    {
        string Value(string key) =>
            data is not null && data.TryGetValue(key, out var v) ? v : string.Empty;

        return template switch
        {
            NotificationTemplate.OtpCode =>
                ("Your Atria code", $"Your verification code is {Value("code")}."),
            NotificationTemplate.KycApproved =>
                ("KYC approved", "Your identity verification has been approved."),
            NotificationTemplate.KycRejected =>
                ("KYC rejected", $"Your identity verification was rejected. {Value("reason")}".TrimEnd()),
            NotificationTemplate.ApplicationSubmitted =>
                ("Application submitted", "Your investment application has been submitted for review."),
            NotificationTemplate.ApplicationApproved =>
                ("Application approved", "Your investment application has been approved."),
            NotificationTemplate.ApplicationRejected =>
                ("Application rejected", $"Your investment application was rejected. {Value("reason")}".TrimEnd()),
            NotificationTemplate.PaymentCompleted =>
                ("Payment received", "We have received your payment. Thank you."),
            NotificationTemplate.InvestmentActivated =>
                ("Investment activated", "Your investment is now active."),
            _ =>
                ("Notification", "You have a new notification.")
        };
    }
}
