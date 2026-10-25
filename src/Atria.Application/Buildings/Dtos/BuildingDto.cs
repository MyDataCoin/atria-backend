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
/// <param name="TotalAreaSqM">Total floor area of the whole building in m²; <c>null</c> when unset.</param>
/// <param name="ConstructionStage">Lowercase: <c>land_only</c> | <c>design</c> | <c>under_construction</c> | <c>commissioned</c> | <c>unspecified</c>.</param>
/// <param name="PlannedCompletionDate">Expected commissioning date; <c>null</c> when there is no schedule.</param>
/// <param name="PlannedCompletionPrecision">How precisely that date was stated: <c>day</c> | <c>month</c> | <c>quarter</c> | <c>year</c>.</param>
/// <param name="HasElevator">The building has a lift.</param>
/// <param name="HasHeating">The building has heating.</param>
/// <param name="HasSecurity">The building has security.</param>
/// <param name="HasParking">The building has parking.</param>
/// <param name="IsFreeOfEncumbrances">Cadastre check result; <c>null</c> when no check is recorded.</param>
/// <param name="EncumbranceCheckedAtUtc">When the cadastre check was made.</param>
/// <param name="IsEditable">Whether the building may still be edited: every unit is a draft or coming soon.</param>
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
    IReadOnlyList<PropertyDto> Units,
    decimal? TotalAreaSqM,
    string ConstructionStage,
    DateTime? PlannedCompletionDate,
    string PlannedCompletionPrecision,
    bool HasElevator,
    bool HasHeating,
    bool HasSecurity,
    bool HasParking,
    bool? IsFreeOfEncumbrances,
    DateTime? EncumbranceCheckedAtUtc,
    bool IsEditable)
{
    /// <summary>Maps a building aggregate plus the units the caller may see to its read model.</summary>
    public static BuildingDto From(Building b, IReadOnlyList<PropertyDto> units)
        => new(b.Id, b.Name, b.Description, b.Address, b.City, b.Developer, b.YearBuilt, b.Floors,
            b.BuildingType,
            b.Images.Select(i => new BuildingImageDto(i.Id, i.Url)).ToList(),
            units.Count, units,
            b.TotalAreaSqM, PropertyDto.ToWireConstructionStage(b.ConstructionStage),
            b.PlannedCompletionDate, PropertyDto.ToWireCompletionDatePrecision(b.PlannedCompletionPrecision),
            b.HasElevator, b.HasHeating, b.HasSecurity, b.HasParking,
            b.IsFreeOfEncumbrances, b.EncumbranceCheckedAtUtc,
            // Units as the caller sees them; the edit itself re-checks against all of them.
            units.All(u => u.Status is "draft" or "coming_soon"));
}
