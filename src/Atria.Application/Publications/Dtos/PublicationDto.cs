using Atria.Domain.Publications;

namespace Atria.Application.Publications.Dtos;

/// <summary>Read model of a news-feed publication.</summary>
/// <param name="Id">Unique identifier of the publication.</param>
/// <param name="Type">Kind, lowercase: <c>financial_report</c> | <c>news_release</c> | <c>valuation_audit</c> | <c>general_news</c>.</param>
/// <param name="Title">Short headline.</param>
/// <param name="Body">Full plain-text body; newlines preserved.</param>
/// <param name="PropertyId">The property the item is about; <c>null</c> for general platform news.</param>
/// <param name="PropertyName">Denormalized property name (join); <c>null</c> for general news.</param>
/// <param name="Status">Lifecycle status, lowercase: <c>draft</c> | <c>published</c>.</param>
/// <param name="PublishedAtUtc">UTC instant the item went live.</param>
/// <param name="CreatedAtUtc">UTC instant the row was created.</param>
/// <param name="Attachments">Reserved for future file attachments; always empty for now.</param>
public sealed record PublicationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    Guid? PropertyId,
    string? PropertyName,
    string Status,
    DateTime PublishedAtUtc,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Attachments)
{
    /// <summary>Maps a domain publication (+ its joined property name) to the wire shape.</summary>
    public static PublicationDto From(Publication p, string? propertyName)
        => new(
            p.Id,
            ToWireType(p.Type),
            p.Title,
            p.Body,
            p.PropertyId,
            propertyName,
            ToWireStatus(p.Status),
            p.PublishedAtUtc,
            p.CreatedAtUtc,
            // Attachments are not implemented yet; the empty array keeps the contract stable.
            Array.Empty<string>());

    /// <summary>Maps the domain type to its lowercase snake_case wire value.</summary>
    public static string ToWireType(PublicationType type) => type switch
    {
        PublicationType.FinancialReport => "financial_report",
        PublicationType.NewsRelease => "news_release",
        PublicationType.ValuationAudit => "valuation_audit",
        _ => "general_news"
    };

    /// <summary>Parses a wire type value; returns false for an unknown one.</summary>
    public static bool TryParseType(string? raw, out PublicationType type)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "financial_report": type = PublicationType.FinancialReport; return true;
            case "news_release": type = PublicationType.NewsRelease; return true;
            case "valuation_audit": type = PublicationType.ValuationAudit; return true;
            case "general_news": type = PublicationType.GeneralNews; return true;
            default: type = default; return false;
        }
    }

    /// <summary>Maps the domain status to its lowercase wire value.</summary>
    public static string ToWireStatus(PublicationStatus status)
        => status == PublicationStatus.Draft ? "draft" : "published";
}
