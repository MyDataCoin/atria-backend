using Atria.Domain.Common;

namespace Atria.Domain.Support;

/// <summary>
/// A single message in a <see cref="SupportTicket"/> thread. Child entity of the ticket
/// aggregate; created only through <see cref="SupportTicket"/>. The author is recorded from
/// the caller's role, not from any client-supplied field.
/// </summary>
public sealed class TicketMessage : Entity
{
    public Guid TicketId { get; private set; }
    public MessageAuthor Author { get; private set; }
    public string Body { get; private set; } = null!;

    private TicketMessage() { }

    internal static TicketMessage Create(Guid ticketId, MessageAuthor author, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new DomainException("Message body is required.");

        return new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            Author = author,
            Body = body
        };
    }
}
