using Atria.Application.Abstractions;
using Atria.Domain.Deals.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the realtor that their referral deal (and link) was created.</summary>
public sealed class DealCreatedNotificationHandler : IDomainEventHandler<DealCreatedEvent>
{
    private readonly INotificationSender _sender;
    private readonly IPropertyRepository _properties;

    public DealCreatedNotificationHandler(INotificationSender sender, IPropertyRepository properties)
    {
        _sender = sender;
        _properties = properties;
    }

    public async Task HandleAsync(DealCreatedEvent domainEvent, CancellationToken ct)
    {
        var data = await DealNotificationData.BuildAsync(
            _properties, domainEvent.DealId, domainEvent.PropertyId, domainEvent.CommissionPercent, ct);
        await _sender.SendAsync(domainEvent.RealtorId, NotificationTemplate.DealCreated, data, ct);
    }
}
