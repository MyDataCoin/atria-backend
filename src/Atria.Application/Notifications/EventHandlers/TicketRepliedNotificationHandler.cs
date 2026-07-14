using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Atria.Domain.Support.Events;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the ticket author that support (admin) replied to their ticket.</summary>
public sealed class TicketRepliedNotificationHandler : IDomainEventHandler<TicketRepliedBySupportEvent>
{
    private readonly INotificationSender _sender;

    public TicketRepliedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(TicketRepliedBySupportEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(
            domainEvent.AuthorId,
            NotificationTemplate.TicketReplied,
            new Dictionary<string, string> { ["subject"] = domainEvent.Subject },
            ct);
}
