using Atria.Domain.Common;
using Atria.Domain.Support;
using FluentAssertions;

namespace Atria.Domain.Tests.Support;

public sealed class SupportTicketStateTests
{
    private static SupportTicket NewTicket()
        => SupportTicket.Open(Guid.NewGuid(), "Can't complete KYC", "KYC", "The upload keeps failing.");

    [Fact]
    public void Open_ProducesOpenTicketWithFirstInvestorMessage()
    {
        var investorId = Guid.NewGuid();

        var ticket = SupportTicket.Open(investorId, "Subject", "Платежи", "Hello");

        ticket.InvestorId.Should().Be(investorId);
        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.Messages.Should().ContainSingle();
        var first = ticket.Messages.Single();
        first.Author.Should().Be(MessageAuthor.Investor);
        first.Body.Should().Be("Hello");
        first.TicketId.Should().Be(ticket.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Open_WhenSubjectMissing_Throws(string subject)
    {
        var act = () => SupportTicket.Open(Guid.NewGuid(), subject, "KYC", "body");

        act.Should().Throw<DomainException>().WithMessage("*subject is required*");
    }

    [Fact]
    public void Open_WhenSubjectTooLong_Throws()
    {
        var subject = new string('x', SupportTicket.MaxSubjectLength + 1);

        var act = () => SupportTicket.Open(Guid.NewGuid(), subject, "KYC", "body");

        act.Should().Throw<DomainException>().WithMessage("*subject cannot exceed*");
    }

    [Fact]
    public void SupportReply_MovesOpenToPending()
    {
        var ticket = NewTicket();

        ticket.AddMessage(MessageAuthor.Support, "Looking into it.");

        ticket.Status.Should().Be(TicketStatus.Pending);
        ticket.Messages.Should().HaveCount(2);
    }

    [Fact]
    public void InvestorReply_MovesPendingBackToOpen()
    {
        var ticket = NewTicket();
        ticket.AddMessage(MessageAuthor.Support, "Any details?"); // -> Pending

        ticket.AddMessage(MessageAuthor.Investor, "Here they are."); // -> Open

        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.Messages.Should().HaveCount(3);
    }

    [Fact]
    public void Close_MovesToClosed()
    {
        var ticket = NewTicket();

        ticket.Close();

        ticket.Status.Should().Be(TicketStatus.Closed);
    }

    [Fact]
    public void Reply_OnClosedTicket_Throws()
    {
        var ticket = NewTicket();
        ticket.Close();

        var act = () => ticket.AddMessage(MessageAuthor.Investor, "still stuck");

        act.Should().Throw<InvalidStateTransitionException>();
        // The rejected reply must not have been recorded.
        ticket.Messages.Should().ContainSingle();
    }

    [Fact]
    public void Close_OnAlreadyClosed_Throws()
    {
        var ticket = NewTicket();
        ticket.Close();

        var act = () => ticket.Close();

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Reopen_MovesClosedToOpen()
    {
        var ticket = NewTicket();
        ticket.Close();

        ticket.Reopen();

        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void Reopen_OnOpenTicket_Throws()
    {
        var ticket = NewTicket();

        var act = () => ticket.Reopen();

        act.Should().Throw<InvalidStateTransitionException>();
    }
}
