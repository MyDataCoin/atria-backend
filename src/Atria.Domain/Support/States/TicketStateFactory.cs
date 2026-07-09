using Atria.Domain.Common;

namespace Atria.Domain.Support.States;

/// <summary>
/// Stateless factory mapping the persisted <see cref="TicketStatus"/> enum to its singleton
/// state object. Keeps EF rehydration to a single column (no _state field).
/// </summary>
public static class TicketStateFactory
{
    public static ITicketState Create(TicketStatus status) => status switch
    {
        TicketStatus.Open => OpenTicketState.Instance,
        TicketStatus.Pending => PendingTicketState.Instance,
        TicketStatus.Closed => ClosedTicketState.Instance,
        _ => throw new InvalidStateTransitionException($"Unknown ticket status: {status}.")
    };
}
