using Atria.Application.Abstractions;
using Atria.Domain.Notifications;
using Atria.Domain.Publications.Events;
using Atria.Domain.Users;

namespace Atria.Application.Notifications.EventHandlers;

/// <summary>
/// Fans a new publication out to its readers: a general item (no property) notifies every active
/// investor, while a property-scoped item notifies only that property's holders (investors with an
/// Active investment in it). Each notification carries the publication id so tapping it can deep-link
/// to the item. Idempotent via the processed-event ledger, so a redelivery never double-notifies.
/// </summary>
public sealed class PublicationPublishedNotificationHandler
    : IDomainEventHandler<PublicationPublishedEvent>
{
    private readonly INotificationSender _sender;
    private readonly IUserRepository _users;
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IProcessedEventStore _processedEvents;

    public PublicationPublishedNotificationHandler(
        INotificationSender sender,
        IUserRepository users,
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IProcessedEventStore processedEvents)
    {
        _sender = sender;
        _users = users;
        _investments = investments;
        _properties = properties;
        _processedEvents = processedEvents;
    }

    public async Task HandleAsync(PublicationPublishedEvent domainEvent, CancellationToken ct)
    {
        var key = IdempotencyKey.For(this, domainEvent.EventId);
        if (await _processedEvents.IsProcessedAsync(key, ct))
            return;

        var recipients = await ResolveRecipientsAsync(domainEvent.PropertyId, ct);
        var data = await BuildDataAsync(domainEvent, ct);

        foreach (var userId in recipients)
        {
            await _sender.SendAsync(
                userId, NotificationTemplate.PublicationPublished, data, ct,
                entityId: domainEvent.PublicationId);
        }

        await _processedEvents.MarkProcessedAsync(key, ct);
    }

    // General news -> every active investor. Property news -> that property's holders only.
    private async Task<IReadOnlyList<Guid>> ResolveRecipientsAsync(Guid? propertyId, CancellationToken ct)
    {
        if (propertyId is not { } id)
            return await _users.GetIdsByRoleAsync(Role.Investor, ct);

        var holders = await _investments.GetActiveByPropertyAsync(id, ct);
        return holders.Select(h => h.InvestorId).Distinct().ToList();
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildDataAsync(
        PublicationPublishedEvent domainEvent, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { ["title"] = domainEvent.Title };

        if (domainEvent.PropertyId is { } propertyId)
        {
            var property = await _properties.GetByIdAsync(propertyId, ct);
            if (property is not null)
                data["propertyName"] = property.Name;
        }

        return data;
    }
}
