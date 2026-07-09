using Atria.Domain.Support;

namespace Atria.Application.Support.Dtos;

/// <summary>Read model of a single ticket message.</summary>
/// <param name="Id">Unique identifier of the message.</param>
/// <param name="Author">Who wrote it: <c>investor</c> or <c>support</c> (derived from role, never the body).</param>
/// <param name="AuthorName">Optional display name for the admin panel; <c>null</c> when unavailable.</param>
/// <param name="Body">The message text.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the message was posted.</param>
public sealed record TicketMessageDto(
    Guid Id,
    string Author,
    string? AuthorName,
    string Body,
    DateTime CreatedAtUtc)
{
    /// <summary>Maps a domain message to its wire shape (author emitted as lowercase per the contract).</summary>
    public static TicketMessageDto From(TicketMessage m, string? authorName = null)
        => new(m.Id, ToWire(m.Author), authorName, m.Body, m.CreatedAtUtc);

    internal static string ToWire(MessageAuthor author) => author switch
    {
        MessageAuthor.Support => "support",
        _ => "investor"
    };
}
