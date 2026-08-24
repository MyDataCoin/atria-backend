using Atria.Domain.Investments;

namespace Atria.Application.Properties.Dtos;

/// <summary>Read model of a property and its current token supply.</summary>
/// <param name="Id">The property's unique identifier.</param>
/// <param name="Name">Display name of the property.</param>
/// <param name="Description">Optional longer description of the property.</param>
/// <param name="TokenPrice">Price of a single token, in the property's currency.</param>
/// <param name="AvailableTokens">Number of tokens still available for investment.</param>
/// <param name="TotalTokens">Total number of tokens the property was issued with.</param>
/// <param name="MinPurchaseTokens">Fewest tokens one application may be for.</param>
/// <param name="MinPurchaseAmount"><paramref name="MinPurchaseTokens"/> × <paramref name="TokenPrice"/> — the smallest sum that buys in.</param>
/// <param name="AreaPerTokenSqM">Area one token stands for (<paramref name="TotalAreaSqM"/> ÷ <paramref name="TotalTokens"/>), or <c>null</c> when the area is unknown. A derived equivalent for display — the token is a share of the issue, not a square metre.</param>
/// <param name="Currency">Currency of the token price. Always <c>KGS</c>.</param>
/// <param name="Status">Lifecycle status, lowercase: <c>draft</c> | <c>coming_soon</c> | <c>open</c> | <c>completed</c>.</param>
/// <param name="SalesPaused">Whether purchases are paused (blocks "buy" on the public site); orthogonal to status.</param>
/// <param name="Address">Physical address; <c>null</c> when unset.</param>
/// <param name="PropertyType">Kind of property (e.g. residential, commercial); <c>null</c> when unset.</param>
/// <param name="City">City the property is in; <c>null</c> when unset.</param>
/// <param name="YearBuilt">Year the property was built; <c>null</c> when unset.</param>
/// <param name="Developer">Developer / builder name; <c>null</c> when unset.</param>
/// <param name="Floors">Number of floors; <c>null</c> when unset.</param>
/// <param name="Images">The property's photos, each with a public URL.</param>
/// <param name="Documents">The property's documents, each with a public URL.</param>
/// <param name="BuildingId">Building this unit belongs to; <c>null</c> for a standalone issue.</param>
/// <param name="UnitType">What the unit is, lowercase: <c>apartment</c> | <c>garage</c> | <c>parking_space</c> | <c>commercial</c> | <c>storage</c> | <c>other</c> | <c>unspecified</c>.</param>
/// <param name="UnitNumber">Unit designation inside the building (flat / garage box number); <c>null</c> when unset.</param>
/// <param name="FloorNumber">Floor the unit is on; <c>null</c> when unset.</param>
/// <param name="RoomCount">How many rooms the unit is sold as (2-, 3-, 4-комнатная); <c>null</c> for a garage.</param>
/// <param name="TotalAreaSqM">Total floor area of the unit in m²; <c>null</c> when unset.</param>
/// <param name="Rooms">Room breakdown (name + area), in the order the admin entered it.</param>
/// <param name="RoomsAreaSqM">Sum of <paramref name="Rooms"/> areas. Compare with <paramref name="TotalAreaSqM"/> to flag a plan that does not add up — the server does not reject a mismatch.</param>
public sealed record PropertyDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TokenPrice,
    long AvailableTokens,
    long TotalTokens,
    long MinPurchaseTokens,
    decimal MinPurchaseAmount,
    decimal? AreaPerTokenSqM,
    string Currency,
    string Status,
    bool SalesPaused,
    string? Address,
    string? PropertyType,
    string? City,
    int? YearBuilt,
    string? Developer,
    int? Floors,
    IReadOnlyList<PropertyImageDto> Images,
    IReadOnlyList<PropertyDocumentDto> Documents,
    Guid? BuildingId,
    string UnitType,
    string? UnitNumber,
    int? FloorNumber,
    int? RoomCount,
    decimal? TotalAreaSqM,
    IReadOnlyList<PropertyRoomDto> Rooms,
    decimal RoomsAreaSqM)
{
    /// <summary>Maps a property aggregate to its read model.</summary>
    public static PropertyDto From(Property p)
    {
        var rooms = p.Rooms
            .OrderBy(r => r.SortOrder)
            .Select(r => new PropertyRoomDto(r.Id, r.Name, r.AreaSqM))
            .ToList();

        return new PropertyDto(
            p.Id, p.Name, p.Description, p.TokenPrice,
            p.AvailableTokens, p.TotalTokens, p.MinPurchaseTokens, p.MinPurchaseAmount,
            p.AreaPerTokenSqM, p.Currency, ToWireStatus(p.Status), p.SalesPaused,
            p.Address, p.PropertyType, p.City, p.YearBuilt, p.Developer, p.Floors,
            p.Images.Select(i => new PropertyImageDto(i.Id, i.Url)).ToList(),
            p.Documents.Select(d => new PropertyDocumentDto(d.Id, d.Url, d.FileName, d.ContentType)).ToList(),
            p.BuildingId, ToWireUnitType(p.UnitType), p.UnitNumber, p.FloorNumber, p.RoomCount,
            p.TotalAreaSqM, rooms, rooms.Sum(r => r.AreaSqM));
    }

    /// <summary>Maps the domain status to its lowercase wire value.</summary>
    public static string ToWireStatus(PropertyStatus status) => status switch
    {
        PropertyStatus.ComingSoon => "coming_soon",
        PropertyStatus.Open => "open",
        PropertyStatus.Completed => "completed",
        _ => "draft"
    };

    /// <summary>Maps the unit type to its lowercase wire value.</summary>
    public static string ToWireUnitType(UnitType unitType) => unitType switch
    {
        Domain.Investments.UnitType.Apartment => "apartment",
        Domain.Investments.UnitType.Garage => "garage",
        Domain.Investments.UnitType.ParkingSpace => "parking_space",
        Domain.Investments.UnitType.Commercial => "commercial",
        Domain.Investments.UnitType.Storage => "storage",
        Domain.Investments.UnitType.Other => "other",
        _ => "unspecified"
    };

    /// <summary>
    /// Parses the wire unit type back to the domain enum. Unknown / absent values map to
    /// <see cref="Domain.Investments.UnitType.Unspecified"/>, which callers treat as "not stated".
    /// </summary>
    public static UnitType ParseUnitType(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "apartment" => Domain.Investments.UnitType.Apartment,
        "garage" => Domain.Investments.UnitType.Garage,
        "parking_space" => Domain.Investments.UnitType.ParkingSpace,
        "commercial" => Domain.Investments.UnitType.Commercial,
        "storage" => Domain.Investments.UnitType.Storage,
        "other" => Domain.Investments.UnitType.Other,
        _ => Domain.Investments.UnitType.Unspecified
    };
}
