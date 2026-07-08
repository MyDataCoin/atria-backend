using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A property document (offering memo, contract, etc.). Child entity of the
/// <see cref="Property"/> aggregate: the bytes live on disk, only the public URL + metadata here.
/// </summary>
public sealed class PropertyDocument : Entity
{
    public Guid PropertyId { get; private set; }
    public string Url { get; private set; } = null!;
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;

    private PropertyDocument() { }

    internal static PropertyDocument Create(Guid propertyId, string url, string fileName, string contentType)
        => new()
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Url = url,
            FileName = fileName,
            ContentType = contentType
        };
}
