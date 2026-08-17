using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Buildings.Dtos;

/// <summary>A building photo: its id (for deletion) and the public URL to display.</summary>
/// <param name="Id">The image's unique identifier.</param>
/// <param name="Url">Public URL of the photo (served statically).</param>
public sealed record BuildingImageDto(Guid Id, string Url);

/// <summary>
/// Read model of a building and the units sold inside it. The building itself has no token supply —
/// each unit in <paramref name="Units"/> carries its own issue.
/// </summary>
/// <param name="Id">The building's unique identifier.</param>
/// <param name="Name">Display name of the building (e.g. "ЖК Ала-Тоо, блок B").</param>
/// <param name="Description">Optional longer description.</param>
/// <param name="Address">Physical address; <c>null</c> when unset.</param>
/// <param name="City">City the building is in; <c>null</c> when unset.</param>
/// <param name="Developer">Developer / builder name; <c>null</c> when unset.</param>
/// <param name="YearBuilt">Year the building was built; <c>null</c> when unset.</param>
/// <param name="Floors">Number of storeys; <c>null</c> when unset.</param>
/// <param name="BuildingType">Kind of building (residential, commercial, mixed); <c>null</c> when unset.</param>
/// <param name="Images">The building's photos, each with a public URL.</param>
/// <param name="UnitCount">How many units are visible to the caller (drafts are staff-only).</param>
/// <param name="Units">The units themselves — apartments, garages — each a property with its own token issue.</param>
public sealed record BuildingDto(
    Guid Id,
    string Name,
    string? Description,
    string? Address,
    string? City,
    string? Developer,
    int? YearBuilt,
    int? Floors,
    string? BuildingType,
    IReadOnlyList<BuildingImageDto> Images,
    int UnitCount,
    IReadOnlyList<PropertyDto> Units)
{
    /// <summary>Maps a building aggregate plus the units the caller may see to its read model.</summary>
    public static BuildingDto From(Building b, IReadOnlyList<PropertyDto> units)
        => new(b.Id, b.Name, b.Description, b.Address, b.City, b.Developer, b.YearBuilt, b.Floors,
            b.BuildingType,
            b.Images.Select(i => new BuildingImageDto(i.Id, i.Url)).ToList(),
            units.Count, units);
}
