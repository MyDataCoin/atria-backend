using Atria.Application.Abstractions;
using Atria.Application.Notifications.EventHandlers;
using Atria.Domain.Deals.Events;
using Atria.Domain.Investments;
using Atria.Domain.Notifications;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Notifications;

/// <summary>
/// Verifies the realtor deal-notification handlers: each addresses the deal's realtor, uses the
/// right template, and passes substitution data resolving the property name (so the body names the
/// object, not a GUID).
/// </summary>
public sealed class DealNotificationHandlersTests
{
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();

    private static readonly Guid RealtorId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private Property StubProperty(string name = "Tower One")
    {
        var property = Property.Create(name, null, null, 1_000_000m, 100m, 10_000, "USD");
        _properties.GetByIdAsync(property.Id, Arg.Any<CancellationToken>()).Returns(property);
        return property;
    }

    [Fact]
    public async Task DealCreated_notifies_the_realtor_with_property_name()
    {
        var property = StubProperty("Bishkek Central");
        var evt = new DealCreatedEvent(Guid.NewGuid(), RealtorId, property.Id, 5m);

        await new DealCreatedNotificationHandler(_sender, _properties).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            RealtorId,
            NotificationTemplate.DealCreated,
            Arg.Is<IReadOnlyDictionary<string, string>>(d =>
                d["propertyName"] == "Bishkek Central" &&
                d["dealId"] == evt.DealId.ToString() &&
                d["commissionPercent"] == "5"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DealSucceeded_notifies_the_realtor()
    {
        var property = StubProperty();
        var evt = new DealSucceededEvent(Guid.NewGuid(), RealtorId, property.Id, Guid.NewGuid(), 7.5m);

        await new DealSucceededNotificationHandler(_sender, _properties).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            RealtorId,
            NotificationTemplate.DealSucceeded,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["commissionPercent"] == "7.5"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DealRejected_notifies_the_realtor()
    {
        var property = StubProperty();
        var evt = new DealRejectedEvent(Guid.NewGuid(), RealtorId, property.Id, 5m);

        await new DealRejectedNotificationHandler(_sender, _properties).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            RealtorId, NotificationTemplate.DealRejected,
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Property_name_is_empty_when_the_property_is_missing()
    {
        var propertyId = Guid.NewGuid();
        _properties.GetByIdAsync(propertyId, Arg.Any<CancellationToken>()).Returns((Property?)null);
        var evt = new DealCreatedEvent(Guid.NewGuid(), RealtorId, propertyId, 5m);

        await new DealCreatedNotificationHandler(_sender, _properties).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            RealtorId, NotificationTemplate.DealCreated,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["propertyName"] == ""),
            Arg.Any<CancellationToken>());
    }
}
