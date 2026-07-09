using Atria.Domain.Common;

namespace Atria.Domain.Support.States;

/// <summary>Ticket is closed. No further replies; it must be reopened first.</summary>
public sealed class ClosedTicketState : ITicketState
{
    public TicketStatus Status => TicketStatus.Closed;

    public ITicketState AddMessage(SupportTicket ticket, MessageAuthor author)
        => throw new InvalidStateTransitionException("Cannot reply to a closed ticket; reopen it first.");

    public ITicketState Close(SupportTicket ticket)
        => throw new InvalidStateTransitionException("Ticket is already closed.");

    public ITicketState Reopen(SupportTicket ticket) => OpenTicketState.Instance;

    public static ClosedTicketState Instance { get; } = new();
    private ClosedTicketState() { }
}
