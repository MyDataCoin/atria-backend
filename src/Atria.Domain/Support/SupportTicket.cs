using Atria.Domain.Common;
using Atria.Domain.Support.States;
using Atria.Domain.Users;

namespace Atria.Domain.Support;

/// <summary>
/// A help-desk ticket opened by an investor. Owns its message thread and is driven
/// through its lifecycle by the State pattern (EF-friendly variant): only the
/// <see cref="TicketStatus"/> enum is persisted and the current state is derived from it.
/// </summary>
public sealed class SupportTicket : AggregateRoot
{
    /// <summary>Maximum length of a ticket subject.</summary>
    public const int MaxSubjectLength = 120;

    public Guid InvestorId { get; private set; }

    /// <summary>
    /// Role of whoever opened the ticket (<see cref="Role.Investor"/> or <see cref="Role.Realtor"/>),
    /// captured from the JWT at creation so the admin desk can tell them apart without depending on a
    /// <c>users</c> row. Defaults to <see cref="Role.Investor"/> for tickets created before this field.
    /// </summary>
    public Role AuthorRole { get; private set; } = Role.Investor;

    public string Subject { get; private set; } = null!;

    /// <summary>Free-form category label chosen on the client (localized display text).</summary>
    public string Category { get; private set; } = null!;

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public TicketStatus Status { get; private set; }

    private readonly List<TicketMessage> _messages = new();
    public IReadOnlyCollection<TicketMessage> Messages => _messages.AsReadOnly();

    // private ctor: creation only through the factory method
    private SupportTicket() { }

    /// <summary>Opens a new ticket (Open) for a client (investor or realtor), seeded with their first message.</summary>
    public static SupportTicket Open(
        Guid investorId, string subject, string category, string body, Role authorRole = Role.Investor)
    {
        if (investorId == Guid.Empty)
            throw new DomainException("Investor is required to open a ticket.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new DomainException("Ticket subject is required.");
        if (subject.Length > MaxSubjectLength)
            throw new DomainException($"Ticket subject cannot exceed {MaxSubjectLength} characters.");
        if (string.IsNullOrWhiteSpace(category))
            throw new DomainException("Ticket category is required.");

        var ticket = new SupportTicket
        {
            Id = Guid.NewGuid(),
            InvestorId = investorId,
            AuthorRole = authorRole,
            Subject = subject,
            Category = category,
            Status = TicketStatus.Open
        };

        ticket._messages.Add(TicketMessage.Create(ticket.Id, MessageAuthor.Investor, body));
        return ticket;
    }

    /// <summary>
    /// Appends a message and moves the status per the State pattern: an investor reply -> Open,
    /// a support reply -> Pending. Throws when the ticket is closed. Returns the new message.
    /// </summary>
    public TicketMessage AddMessage(MessageAuthor author, string body)
    {
        // State decides the resulting status (and rejects replies on a closed ticket) BEFORE
        // the message is recorded, so a rejected transition leaves the thread untouched.
        var next = TicketStateFactory.Create(Status).AddMessage(this, author);

        var message = TicketMessage.Create(Id, author, body);
        _messages.Add(message);
        Status = next.Status;
        return message;
    }

    /// <summary>Closes the ticket. Terminal until reopened.</summary>
    public void Close()
        => Status = TicketStateFactory.Create(Status).Close(this).Status;

    /// <summary>Reopens a closed ticket (Closed -> Open). Admin action.</summary>
    public void Reopen()
        => Status = TicketStateFactory.Create(Status).Reopen(this).Status;
}
