using Atria.Application.Abstractions;
using Atria.Domain.Kyc.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the user that their KYC was approved.</summary>
public sealed class KycApprovedNotificationHandler : IDomainEventHandler<KycApprovedEvent>
{
    private readonly INotificationSender _sender;

    public KycApprovedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(KycApprovedEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(domainEvent.UserId, NotificationTemplate.KycApproved, data: null, ct);
}
