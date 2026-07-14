using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Atria.Domain.Support.Events;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Confirms to the ticket author (investor or realtor) that their ticket was submitted.</summary>
public sealed class TicketOpenedNotificationHandler : IDomainEventHandler<TicketOpenedEvent>
{
    private readonly INotificationSender _sender;

    public TicketOpenedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(TicketOpenedEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(
            domainEvent.AuthorId,
            NotificationTemplate.TicketOpened,
            new Dictionary<string, string> { ["subject"] = domainEvent.Subject },
            ct);
}
