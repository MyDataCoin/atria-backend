using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A property photo. Child entity of the <see cref="Property"/> aggregate: the bytes live on
/// disk (served statically), only the public URL is stored here.
/// </summary>
public sealed class PropertyImage : Entity
{
    public Guid PropertyId { get; private set; }
    public string Url { get; private set; } = null!;

    private PropertyImage() { }

    internal static PropertyImage Create(Guid propertyId, string url)
        => new() { Id = Guid.NewGuid(), PropertyId = propertyId, Url = url };
}
