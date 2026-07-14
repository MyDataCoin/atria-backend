using Atria.Application.Abstractions;
using Atria.Domain.Deals.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the realtor that their referral link expired unused and the deal was rejected.</summary>
public sealed class DealRejectedNotificationHandler : IDomainEventHandler<DealRejectedEvent>
{
    private readonly INotificationSender _sender;
    private readonly IPropertyRepository _properties;

    public DealRejectedNotificationHandler(INotificationSender sender, IPropertyRepository properties)
    {
        _sender = sender;
        _properties = properties;
    }

    public async Task HandleAsync(DealRejectedEvent domainEvent, CancellationToken ct)
    {
        var data = await DealNotificationData.BuildAsync(
            _properties, domainEvent.DealId, domainEvent.PropertyId, domainEvent.CommissionPercent, ct);
        await _sender.SendAsync(domainEvent.RealtorId, NotificationTemplate.DealRejected, data, ct);
    }
}
