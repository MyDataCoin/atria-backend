namespace Atria.Domain.Support.States;

/// <summary>
/// State pattern for a <see cref="SupportTicket"/> (EF-friendly variant). State objects are
/// stateless singletons; the transition methods encapsulate the allowed moves and return the
/// next state. The aggregate records the message; the state only decides the resulting status.
/// </summary>
public interface ITicketState
{
    TicketStatus Status { get; }

    /// <summary>Investor reply -> Open, support reply -> Pending. Rejected on a closed ticket.</summary>
    ITicketState AddMessage(SupportTicket ticket, MessageAuthor author);

    /// <summary>Any open/pending ticket -> Closed.</summary>
    ITicketState Close(SupportTicket ticket);

    /// <summary>Closed -> Open.</summary>
    ITicketState Reopen(SupportTicket ticket);
}
