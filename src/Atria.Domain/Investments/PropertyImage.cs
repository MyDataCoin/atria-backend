using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A property image. Child entity of the <see cref="Property"/> aggregate: the bytes live on
/// disk (served statically), only the public URL is stored here.
/// </summary>
public sealed class PropertyImage : Entity
{
    /// <summary>Longest a caption may be.</summary>
    public const int MaxCaption = 200;

    public Guid PropertyId { get; private set; }
    public string Url { get; private set; } = null!;

    /// <summary>
    /// What the image is — a photograph, a render, a plan. Defaults to
    /// <see cref="PropertyImageKind.Photo"/>, which is what every image uploaded before this field
    /// existed was: the objects were built.
    /// </summary>
    public PropertyImageKind Kind { get; private set; } = PropertyImageKind.Photo;

    /// <summary>The uploader's caption, or null. Free text shown under the image.</summary>
    public string? Caption { get; private set; }

    /// <summary>Position in the gallery. The lowest is the cover.</summary>
    public int SortOrder { get; private set; }

    private PropertyImage() { }

    internal static PropertyImage Create(
        Guid propertyId, string url, PropertyImageKind kind, string? caption, int sortOrder)
    {
        var trimmed = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (trimmed is { Length: > MaxCaption })
            throw new DomainException($"A caption cannot exceed {MaxCaption} characters.");

        return new PropertyImage
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Url = url,
            Kind = kind,
            Caption = trimmed,
            SortOrder = sortOrder
        };
    }

    /// <summary>Moves the image within the gallery. Position 0 is the cover.</summary>
    internal void MoveTo(int sortOrder)
    {
        if (sortOrder < 0)
            throw new DomainException("Sort order cannot be negative.");

        SortOrder = sortOrder;
    }
}
