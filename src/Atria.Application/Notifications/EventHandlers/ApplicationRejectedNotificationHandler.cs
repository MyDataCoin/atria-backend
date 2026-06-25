using Atria.Application.Abstractions;
using Atria.Domain.Applications.Events;
using Atria.Domain.Notifications;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>Notifies the investor that their application was rejected, passing the reason.</summary>
public sealed class ApplicationRejectedNotificationHandler
    : IDomainEventHandler<ApplicationRejectedEvent>
{
    private readonly INotificationSender _sender;

    public ApplicationRejectedNotificationHandler(INotificationSender sender) => _sender = sender;

    public Task HandleAsync(ApplicationRejectedEvent domainEvent, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { ["reason"] = domainEvent.Reason };
        return _sender.SendAsync(domainEvent.InvestorId, NotificationTemplate.ApplicationRejected, data, ct);
    }
}
