using Atria.Application.Abstractions;
using Atria.Application.Notifications.EventHandlers;
using Atria.Domain.Notifications;
using Atria.Domain.Support.Events;
using NSubstitute;

namespace Atria.Application.Tests.Notifications;

/// <summary>
/// Verifies the author-facing ticket notification handlers: each addresses the ticket author, uses
/// the right template, and passes the subject as substitution data.
/// </summary>
public sealed class TicketNotificationHandlersTests
{
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();

    [Fact]
    public async Task TicketOpened_notifies_the_author()
    {
        var authorId = Guid.NewGuid();
        var evt = new TicketOpenedEvent(Guid.NewGuid(), authorId, "Referral link broken");

        await new TicketOpenedNotificationHandler(_sender).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            authorId, NotificationTemplate.TicketOpened,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["subject"] == "Referral link broken"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TicketReplied_notifies_the_author()
    {
        var authorId = Guid.NewGuid();
        var evt = new TicketRepliedBySupportEvent(Guid.NewGuid(), authorId, "Payment stuck");

        await new TicketRepliedNotificationHandler(_sender).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            authorId, NotificationTemplate.TicketReplied,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["subject"] == "Payment stuck"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TicketClosed_notifies_the_author()
    {
        var authorId = Guid.NewGuid();
        var evt = new TicketClosedEvent(Guid.NewGuid(), authorId, "KYC issue");

        await new TicketClosedNotificationHandler(_sender).HandleAsync(evt, CancellationToken.None);

        await _sender.Received(1).SendAsync(
            authorId, NotificationTemplate.TicketClosed,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d["subject"] == "KYC issue"),
            Arg.Any<CancellationToken>());
    }
}
