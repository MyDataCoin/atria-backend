namespace Atria.Domain.Support;

/// <summary>
/// Support ticket lifecycle. A new ticket is <see cref="Open"/>; a support reply moves it to
/// <see cref="Pending"/> (waiting on the investor), an investor reply moves it back to
/// <see cref="Open"/>, and closing it makes it <see cref="Closed"/>.
/// </summary>
public enum TicketStatus
{
    Open = 0,
    Pending = 1,
    Closed = 2
}
