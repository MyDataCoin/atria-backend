namespace Atria.Application.Properties.Dtos;

/// <summary>A property image: its id (for deletion), the public URL, and what the image actually is.</summary>
/// <param name="Id">The image's unique identifier.</param>
/// <param name="Url">Public URL of the image (served statically).</param>
/// <param name="Kind">
/// What it is, lowercase: <c>photo</c> | <c>render</c> | <c>floor_plan</c> | <c>site_plan</c>.
/// A <c>render</c> must be labelled as a visualisation wherever it is shown — it is a picture of
/// something that does not exist yet.
/// </param>
/// <param name="Caption">The uploader's caption, or <c>null</c>.</param>
public sealed record PropertyImageDto(Guid Id, string Url, string Kind, string? Caption)
{
    /// <summary>Maps an image to its read model.</summary>
    public static PropertyImageDto From(Domain.Investments.PropertyImage image)
        => new(image.Id, image.Url, ToWireKind(image.Kind), image.Caption);

    /// <summary>Maps the image kind to its lowercase wire value.</summary>
    public static string ToWireKind(Domain.Investments.PropertyImageKind kind) => kind switch
    {
        Domain.Investments.PropertyImageKind.Render => "render",
        Domain.Investments.PropertyImageKind.FloorPlan => "floor_plan",
        Domain.Investments.PropertyImageKind.SitePlan => "site_plan",
        _ => "photo"
    };

    /// <summary>
    /// Parses a wire image kind. Anything unrecognised is a photo, which is what an image with no
    /// stated kind has always been.
    /// </summary>
    public static Domain.Investments.PropertyImageKind ParseKind(string? wire)
        => wire?.Trim().ToLowerInvariant() switch
        {
            "render" => Domain.Investments.PropertyImageKind.Render,
            "floor_plan" => Domain.Investments.PropertyImageKind.FloorPlan,
            "site_plan" => Domain.Investments.PropertyImageKind.SitePlan,
            _ => Domain.Investments.PropertyImageKind.Photo
        };
}

/// <summary>A property document: id, public URL, original file name and content type.</summary>
/// <param name="Id">The document's unique identifier.</param>
/// <param name="Url">Public URL of the document (served statically).</param>
/// <param name="FileName">Original uploaded file name.</param>
/// <param name="ContentType">MIME content type of the document.</param>
/// <param name="Category">What the document is, lowercase: <c>legal</c> | <c>technical_passport</c> | <c>valuation</c> | <c>collateral</c> | <c>construction_schedule</c> | <c>layout</c> | <c>unspecified</c>.</param>
/// <param name="Title">What to call it in a list; <c>null</c> when it was never given a name.</param>
/// <param name="DisplayName"><paramref name="Title"/>, or <paramref name="FileName"/> when there is none. What a list should render.</param>
public sealed record PropertyDocumentDto(
    Guid Id, string Url, string FileName, string ContentType,
    string Category, string? Title, string DisplayName)
{
    /// <summary>Maps a document to its read model.</summary>
    public static PropertyDocumentDto From(Domain.Investments.PropertyDocument d)
        => new(d.Id, d.Url, d.FileName, d.ContentType, ToWireCategory(d.Category), d.Title, d.DisplayName);

    /// <summary>Maps the document category to its lowercase wire value.</summary>
    public static string ToWireCategory(Domain.Investments.PropertyDocumentCategory category) => category switch
    {
        Domain.Investments.PropertyDocumentCategory.Legal => "legal",
        Domain.Investments.PropertyDocumentCategory.TechnicalPassport => "technical_passport",
        Domain.Investments.PropertyDocumentCategory.Valuation => "valuation",
        Domain.Investments.PropertyDocumentCategory.Collateral => "collateral",
        Domain.Investments.PropertyDocumentCategory.ConstructionSchedule => "construction_schedule",
        Domain.Investments.PropertyDocumentCategory.Layout => "layout",
        _ => "unspecified"
    };

    /// <summary>
    /// Parses the wire category. Unknown / absent values map to
    /// <see cref="Domain.Investments.PropertyDocumentCategory.Unspecified"/> — an unrecognised label
    /// must not lose the upload, only its filing.
    /// </summary>
    public static Domain.Investments.PropertyDocumentCategory ParseCategory(string? wire)
        => wire?.Trim().ToLowerInvariant() switch
        {
            "legal" => Domain.Investments.PropertyDocumentCategory.Legal,
            "technical_passport" => Domain.Investments.PropertyDocumentCategory.TechnicalPassport,
            "valuation" => Domain.Investments.PropertyDocumentCategory.Valuation,
            "collateral" => Domain.Investments.PropertyDocumentCategory.Collateral,
            "construction_schedule" => Domain.Investments.PropertyDocumentCategory.ConstructionSchedule,
            "layout" => Domain.Investments.PropertyDocumentCategory.Layout,
            _ => Domain.Investments.PropertyDocumentCategory.Unspecified
        };
}

/// <summary>One line of a unit's room breakdown — "Кухня+Столовая — 28,68 м²".</summary>
/// <param name="Id">The room row's unique identifier.</param>
/// <param name="Name">Room label as the admin typed it.</param>
/// <param name="AreaSqM">Floor area of the room in square metres.</param>
public sealed record PropertyRoomDto(Guid Id, string Name, decimal AreaSqM);
