using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A property document (offering memo, contract, etc.). Child entity of the
/// <see cref="Property"/> aggregate: the bytes live on disk, only the public URL + metadata here.
/// </summary>
public sealed class PropertyDocument : Entity
{
    /// <summary>Longest a document title may be.</summary>
    public const int MaxTitle = 200;

    public Guid PropertyId { get; private set; }
    public string Url { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;

    /// <summary>
    /// What the document is. <see cref="PropertyDocumentCategory.Unspecified"/> for everything
    /// uploaded before the category existed.
    /// </summary>
    public PropertyDocumentCategory Category { get; private set; } = PropertyDocumentCategory.Unspecified;

    /// <summary>
    /// What to call the document in a list, when a person gave it a name. Null falls back to
    /// <see cref="FileName"/>.
    /// </summary>
    /// <remarks>
    /// Kept apart from the file name: "Технический паспорт" is what a reader is looking for and
    /// "scan_0012_final(2).pdf" is what came off the scanner. Overwriting one with the other loses
    /// either the meaning or the actual file.
    /// </remarks>
    public string? Title { get; private set; }

    /// <summary>What to show in a list: the given title, or the file name when there is none.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? FileName : Title;

    private PropertyDocument() { }

    internal static PropertyDocument Create(
        Guid propertyId, string url, string fileName, string contentType,
        PropertyDocumentCategory category = PropertyDocumentCategory.Unspecified,
        string? title = null)
    {
        var trimmed = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        if (trimmed is { Length: > MaxTitle })
            throw new DomainException($"A document title cannot exceed {MaxTitle} characters.");

        return new()
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Url = url,
            FileName = fileName,
            ContentType = contentType,
            Category = category,
            Title = trimmed
        };
    }
}
