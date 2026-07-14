using Atria.Application.Abstractions;
using Atria.Domain.Deals.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>
/// Notifies the realtor that an investor bought through their referral link and the deal completed
/// — the moment the realtor earns their commission.
/// </summary>
public sealed class DealSucceededNotificationHandler : IDomainEventHandler<DealSucceededEvent>
{
    private readonly INotificationSender _sender;
    private readonly IPropertyRepository _properties;

    public DealSucceededNotificationHandler(INotificationSender sender, IPropertyRepository properties)
    {
        _sender = sender;
        _properties = properties;
    }

    public async Task HandleAsync(DealSucceededEvent domainEvent, CancellationToken ct)
    {
        var data = await DealNotificationData.BuildAsync(
            _properties, domainEvent.DealId, domainEvent.PropertyId, domainEvent.CommissionPercent, ct);
        await _sender.SendAsync(domainEvent.RealtorId, NotificationTemplate.DealSucceeded, data, ct);
    }
}
