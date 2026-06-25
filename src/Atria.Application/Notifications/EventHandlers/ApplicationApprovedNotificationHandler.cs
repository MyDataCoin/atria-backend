using Atria.Application.Abstractions;
using Atria.Domain.Applications.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the investor that their application was approved.</summary>
public sealed class ApplicationApprovedNotificationHandler
    : IDomainEventHandler<ApplicationApprovedEvent>
{
    private readonly INotificationSender _sender;

    public ApplicationApprovedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(ApplicationApprovedEvent domainEvent, CancellationToken ct)
        => _sender.SendAsync(domainEvent.InvestorId, NotificationTemplate.ApplicationApproved, data: null, ct);
}
