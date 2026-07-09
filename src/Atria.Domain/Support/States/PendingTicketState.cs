using Atria.Domain.Common;

namespace Atria.Domain.Support.States;

/// <summary>Support has replied; waiting on the investor. Replies keep it moving; it can be closed.</summary>
public sealed class PendingTicketState : ITicketState
{
    public TicketStatus Status => TicketStatus.Pending;

    public ITicketState AddMessage(SupportTicket ticket, MessageAuthor author)
        => author == MessageAuthor.Support ? Instance : OpenTicketState.Instance;

    public ITicketState Close(SupportTicket ticket) => ClosedTicketState.Instance;

    public ITicketState Reopen(SupportTicket ticket)
        => throw new InvalidStateTransitionException("Ticket is not closed.");

    public static PendingTicketState Instance { get; } = new();
    private PendingTicketState() { }
}
