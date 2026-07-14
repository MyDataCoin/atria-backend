using System.Linq;
using Atria.Domain.Support;
using Atria.Domain.Support.Events;
using FluentAssertions;

namespace Atria.Domain.Tests.Support;

/// <summary>
/// Domain events that drive author-facing ticket notifications: opening raises TicketOpened, a
/// support reply raises TicketRepliedBySupport (an author reply does not), and closing raises
/// TicketClosed — all addressed to the ticket's author (InvestorId).
/// </summary>
public sealed class SupportTicketEventsTests
{
    private static SupportTicket NewTicket(Guid authorId)
        => SupportTicket.Open(authorId, "Referral link broken", "Deals", "It returns 404.");

    [Fact]
    public void Open_RaisesTicketOpened_AddressedToAuthor()
    {
        var authorId = Guid.NewGuid();

        var ticket = NewTicket(authorId);

        ticket.DomainEvents.OfType<TicketOpenedEvent>().Should().ContainSingle()
            .Which.AuthorId.Should().Be(authorId);
    }

    [Fact]
    public void SupportReply_RaisesRepliedEvent_AddressedToAuthor()
    {
        var authorId = Guid.NewGuid();
        var ticket = NewTicket(authorId);
        ticket.ClearEvents();

        ticket.AddMessage(MessageAuthor.Support, "We're on it.");

        ticket.DomainEvents.OfType<TicketRepliedBySupportEvent>().Should().ContainSingle()
            .Which.AuthorId.Should().Be(authorId);
    }

    [Fact]
    public void AuthorReply_DoesNotRaiseRepliedEvent()
    {
        var ticket = NewTicket(Guid.NewGuid());
        ticket.ClearEvents();

        ticket.AddMessage(MessageAuthor.Investor, "Any update?");

        ticket.DomainEvents.OfType<TicketRepliedBySupportEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Close_RaisesTicketClosed_AddressedToAuthor()
    {
        var authorId = Guid.NewGuid();
        var ticket = NewTicket(authorId);
        ticket.ClearEvents();

        ticket.Close();

        ticket.DomainEvents.OfType<TicketClosedEvent>().Should().ContainSingle()
            .Which.AuthorId.Should().Be(authorId);
    }
}
