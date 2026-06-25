using Atria.Application.Abstractions;
using Atria.Domain.Kyc.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the user that their KYC was rejected, passing the reason for rendering.</summary>
public sealed class KycRejectedNotificationHandler : IDomainEventHandler<KycRejectedEvent>
{
    private readonly INotificationSender _sender;

    public KycRejectedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(KycRejectedEvent domainEvent, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { ["reason"] = domainEvent.Reason };
        return _sender.SendAsync(domainEvent.UserId, NotificationTemplate.KycRejected, data, ct);
    }
}
