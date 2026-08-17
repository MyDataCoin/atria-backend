using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A building photo. Child entity of the <see cref="Building"/> aggregate: the bytes live on
/// disk (served statically), only the public URL is stored here.
/// </summary>
public sealed class BuildingImage : Entity
{
    public Guid BuildingId { get; private set; }
    public string Url { get; private set; } = null!;

    private BuildingImage() { }

    internal static BuildingImage Create(Guid buildingId, string url)
        => new() { Id = Guid.NewGuid(), BuildingId = buildingId, Url = url };
}
