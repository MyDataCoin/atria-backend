namespace Atria.Application.Properties.Dtos;

/// <summary>A property photo: its id (for deletion) and the public URL to display.</summary>
/// <param name="Id">The image's unique identifier.</param>
/// <param name="Url">Public URL of the photo (served statically).</param>
public sealed record PropertyImageDto(Guid Id, string Url);

/// <summary>A property document: id, public URL, original file name and content type.</summary>
/// <param name="Id">The document's unique identifier.</param>
/// <param name="Url">Public URL of the document (served statically).</param>
/// <param name="FileName">Original uploaded file name.</param>
/// <param name="ContentType">MIME content type of the document.</param>
public sealed record PropertyDocumentDto(Guid Id, string Url, string FileName, string ContentType);

/// <summary>One line of a unit's room breakdown — "Кухня+Столовая — 28,68 м²".</summary>
/// <param name="Id">The room row's unique identifier.</param>
/// <param name="Name">Room label as the admin typed it.</param>
/// <param name="AreaSqM">Floor area of the room in square metres.</param>
public sealed record PropertyRoomDto(Guid Id, string Name, decimal AreaSqM);
