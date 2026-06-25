using Atria.Application.Abstractions;
using Atria.Domain.Investments.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the investor that their investment is now active.</summary>
public sealed class InvestmentActivatedNotificationHandler
    : IDomainEventHandler<InvestmentActivatedEvent>
{
    private readonly INotificationSender _sender;

    public InvestmentActivatedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(InvestmentActivatedEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(domainEvent.InvestorId, NotificationTemplate.InvestmentActivated, data: null, ct);
}
