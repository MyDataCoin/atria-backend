using Atria.Domain.Common;

namespace Atria.Domain.Support.States;

/// <summary>Ticket is open (awaiting support). Replies keep it moving; it can be closed.</summary>
public sealed class OpenTicketState : ITicketState
{
    public TicketStatus Status => TicketStatus.Open;

    public ITicketState AddMessage(SupportTicket ticket, MessageAuthor author)
        => author == MessageAuthor.Support ? PendingTicketState.Instance : Instance;

    public ITicketState Close(SupportTicket ticket) => ClosedTicketState.Instance;

    public ITicketState Reopen(SupportTicket ticket)
        => throw new InvalidStateTransitionException("Ticket is already open.");

    public static OpenTicketState Instance { get; } = new();
    private OpenTicketState() { }
}
