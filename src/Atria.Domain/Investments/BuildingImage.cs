using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A building image. Child entity of the <see cref="Building"/> aggregate: the bytes live on
/// disk (served statically), only the public URL is stored here.
/// </summary>
public sealed class BuildingImage : Entity
{
    public Guid BuildingId { get; private set; }
    public string Url { get; private set; } = null!;

    /// <summary>
    /// What the image is. Defaults to <see cref="PropertyImageKind.Photo"/>, which is what every
    /// building image uploaded before this field existed was. A render of a building under
    /// construction has to say so: the first image is the building's cover on the site.
    /// </summary>
    public PropertyImageKind Kind { get; private set; } = PropertyImageKind.Photo;

    /// <summary>The uploader's caption, or null. Free text shown with the image.</summary>
    public string? Caption { get; private set; }

    /// <summary>Position in the gallery. The lowest is the cover.</summary>
    public int SortOrder { get; private set; }

    private BuildingImage() { }

    internal static BuildingImage Create(
        Guid buildingId, string url, PropertyImageKind kind, string? caption, int sortOrder)
    {
        var trimmed = string.IsNullOrWhiteSpace(caption) ? null : caption.Trim();
        if (trimmed is { Length: > PropertyImage.MaxCaption })
            throw new DomainException($"A caption cannot exceed {PropertyImage.MaxCaption} characters.");

        return new BuildingImage
        {
            Id = Guid.NewGuid(),
            BuildingId = buildingId,
            Url = url,
            Kind = kind,
            Caption = trimmed,
            SortOrder = sortOrder
        };
    }
}
