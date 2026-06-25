using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the investor that their payment completed.</summary>
public sealed class PaymentCompletedNotificationHandler
    : IDomainEventHandler<PaymentCompletedEvent>
{
    private readonly INotificationSender _sender;

    public PaymentCompletedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(PaymentCompletedEvent domainEvent, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["amount"] = domainEvent.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        return _sender.SendAsync(domainEvent.InvestorId, NotificationTemplate.PaymentCompleted, data, ct);
    }
}
