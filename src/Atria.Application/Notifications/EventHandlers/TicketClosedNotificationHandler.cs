using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Atria.Domain.Support.Events;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the ticket author that their ticket was closed (resolved).</summary>
public sealed class TicketClosedNotificationHandler : IDomainEventHandler<TicketClosedEvent>
{
    private readonly INotificationSender _sender;

    public TicketClosedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(TicketClosedEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(
            domainEvent.AuthorId,
            NotificationTemplate.TicketClosed,
            new Dictionary<string, string> { ["subject"] = domainEvent.Subject },
            ct);
}
