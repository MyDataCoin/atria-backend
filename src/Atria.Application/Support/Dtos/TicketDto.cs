using Atria.Domain.Support;

namespace Atria.Application.Support.Dtos;

/// <summary>Read model of a support ticket.</summary>
/// <param name="Id">Unique identifier of the ticket.</param>
/// <param name="Subject">Short subject line.</param>
/// <param name="Category">Category label chosen on the client.</param>
/// <param name="Status">Lifecycle status, lowercase: <c>open</c> | <c>pending</c> | <c>closed</c>.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the ticket was opened.</param>
/// <param name="UpdatedAtUtc">UTC timestamp of the last activity (falls back to creation time).</param>
/// <param name="Investor">Who opened it (Admin views only); <c>null</c> for the owning investor's own view.</param>
/// <param name="Messages">The message thread (oldest first); <c>null</c> on the list route.</param>
public sealed record TicketDto(
    Guid Id,
    string Subject,
    string Category,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    TicketInvestorDto? Investor,
    IReadOnlyList<TicketMessageDto>? Messages)
{
    /// <summary>Maps a domain ticket to its wire shape. Pass <paramref name="investor"/> for Admin views and
    /// <paramref name="includeMessages"/> to embed the thread (detail route).</summary>
    /// <param name="clientAuthorName">Display name for the client's own messages (admin thread view);
    /// applied to <c>investor</c>-authored messages, not <c>support</c> ones. Null for a client's own view.</param>
    public static TicketDto From(
        SupportTicket t, TicketInvestorDto? investor = null, bool includeMessages = false,
        string? clientAuthorName = null)
        => new(
            t.Id,
            t.Subject,
            t.Category,
            ToWire(t.Status),
            t.CreatedAtUtc,
            t.UpdatedAtUtc ?? t.CreatedAtUtc,
            investor,
            includeMessages
                ? t.Messages.OrderBy(m => m.CreatedAtUtc)
                    .Select(m => TicketMessageDto.From(
                        m, m.Author == Atria.Domain.Support.MessageAuthor.Investor ? clientAuthorName : null))
                    .ToList()
                : null);

    internal static string ToWire(TicketStatus status) => status switch
    {
        TicketStatus.Pending => "pending",
        TicketStatus.Closed => "closed",
        _ => "open"
    };
}
