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
/// <param name="AreaPerTokenSqM">Area one token stands for (<paramref name="OfferedAreaSqM"/> ÷ <paramref name="TotalTokens"/>, falling back to <paramref name="TotalAreaSqM"/>), or <c>null</c> when there is no area to divide. A derived equivalent for display — the token is a share of the issue, not a square metre.</param>
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
/// <param name="Section">Car-park section a garage / parking space sits in (e.g. <c>B</c>); <c>null</c> when unset.</param>
/// <param name="Row">Row within the section (e.g. <c>12А</c>); <c>null</c> when unset.</param>
/// <param name="Spot">The parking space's own number (e.g. <c>125</c>); <c>null</c> when unset.</param>
/// <param name="TotalAreaSqM">Total floor area of the unit in m²; <c>null</c> when unset.</param>
/// <param name="UsableAreaSqM">Usable floor area in m² — occupiable space, without walls and common parts; <c>null</c> when unset.</param>
/// <param name="DocumentedUse">Permitted use as written in the title documents; <c>null</c> when unset. Distinct from <paramref name="PropertyType"/>, which is the catalogue filter.</param>
/// <param name="BuildingClass">Class of the object (e.g. <c>бизнес</c>); <c>null</c> when unset.</param>
/// <param name="WallMaterial">Construction material; <c>null</c> when unset.</param>
/// <param name="Heating">Heating arrangement; <c>null</c> when unset.</param>
/// <param name="Elevator">Lifts, as described; <c>null</c> when unset.</param>
/// <param name="Security">Security arrangement; <c>null</c> when unset.</param>
/// <param name="Parking">Parking available with the object; <c>null</c> when unset.</param>
/// <param name="OfferedAreaSqM">Area being placed in m² when the issue covers only part of the object; <c>null</c> when the whole unit is issued. Frozen once shares are placed.</param>
/// <param name="LandAreaHectares">Area of the land plot in hectares; <c>null</c> when unset. Not floor area — <paramref name="AreaPerTokenSqM"/> is not derived from it.</param>
/// <param name="LandPlotCode">The plot's identification code in the state cadastre (e.g. <c>1-04-13-0033-0135</c>); <c>null</c> when unset.</param>
/// <param name="CadastralNumber">Cadastral number of the built object; <c>null</c> when there is none yet.</param>
/// <param name="ConstructionStage">How far along the object is, lowercase: <c>land_only</c> | <c>design</c> | <c>under_construction</c> | <c>commissioned</c> | <c>unspecified</c>. Independent of <paramref name="Status"/>, which tracks the placement.</param>
/// <param name="PlannedCompletionDate">Expected commissioning date; <c>null</c> when there is no schedule.</param>
/// <param name="ReadinessPercent">Reported construction readiness, 0–100; <c>null</c> when not reported.</param>
/// <param name="IsFreeOfEncumbrances">Cadastre check result: <c>true</c> when free of encumbrances and arrests, <c>false</c> when something was found, <c>null</c> when no check is recorded.</param>
/// <param name="EncumbranceCheckedAtUtc">When the cadastre check was made; <c>null</c> when none is recorded.</param>
/// <param name="PlacementOpensAtUtc">When the placement is scheduled to open; <c>null</c> when it is opened by hand.</param>
/// <param name="PlacementClosesAtUtc">When it is scheduled to close; <c>null</c> when closed by hand.</param>
/// <param name="TargetAmount">The sum the placement is trying to raise; <c>null</c> when it has no target.</param>
/// <param name="RaisedAmount">Placed so far: shares taken out of supply, at the issue price.</param>
/// <param name="IsTargetMet">Whether <paramref name="RaisedAmount"/> has reached <paramref name="TargetAmount"/>. Always <c>true</c> when there is no target.</param>
/// <param name="PlacementExtensionCount">How many times the closing date has been pushed back.</param>
/// <param name="PayoutFrequency">How often the issue distributes, lowercase: <c>none</c> | <c>monthly</c> | <c>quarterly</c> | <c>annually</c> | <c>unspecified</c>.</param>
/// <param name="DistributesYet">
/// Whether the issue pays anything out yet. <c>false</c> while it is not commissioned, whatever
/// frequency was entered — public surfaces should read this and not <paramref name="PayoutFrequency"/>,
/// so an object under construction does not advertise a payment it cannot make.
/// </param>
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
    string? Section,
    string? Row,
    string? Spot,
    decimal? TotalAreaSqM,
    decimal? UsableAreaSqM,
    string? DocumentedUse,
    string? BuildingClass,
    string? WallMaterial,
    string? Heating,
    string? Elevator,
    string? Security,
    string? Parking,
    decimal? OfferedAreaSqM,
    decimal? LandAreaHectares,
    string? LandPlotCode,
    string? CadastralNumber,
    string ConstructionStage,
    DateTime? PlannedCompletionDate,
    int? ReadinessPercent,
    bool? IsFreeOfEncumbrances,
    DateTime? EncumbranceCheckedAtUtc,
    DateTime? PlacementOpensAtUtc,
    DateTime? PlacementClosesAtUtc,
    decimal? TargetAmount,
    decimal RaisedAmount,
    bool IsTargetMet,
    int PlacementExtensionCount,
    string PayoutFrequency,
    bool DistributesYet,
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
            p.Images.Select(PropertyImageDto.From).ToList(),
            p.Documents.Select(d => new PropertyDocumentDto(d.Id, d.Url, d.FileName, d.ContentType)).ToList(),
            p.BuildingId, ToWireUnitType(p.UnitType), p.UnitNumber, p.FloorNumber, p.RoomCount,
            p.Section, p.Row, p.Spot,
            p.TotalAreaSqM, p.UsableAreaSqM, p.DocumentedUse, p.BuildingClass, p.WallMaterial,
            p.Heating, p.Elevator, p.Security, p.Parking,
            p.OfferedAreaSqM, p.LandAreaHectares, p.LandPlotCode, p.CadastralNumber,
            ToWireConstructionStage(p.ConstructionStage), p.PlannedCompletionDate,
            p.ReadinessPercent, p.IsFreeOfEncumbrances, p.EncumbranceCheckedAtUtc,
            p.PlacementOpensAtUtc, p.PlacementClosesAtUtc, p.TargetAmount,
            p.RaisedAmount, p.IsTargetMet, p.PlacementExtensionCount,
            ToWirePayoutFrequency(p.PayoutFrequency), p.DistributesYet,
            rooms, rooms.Sum(r => r.AreaSqM));
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
        Domain.Investments.UnitType.LandPlot => "land_plot",
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
        "land_plot" => Domain.Investments.UnitType.LandPlot,
        "other" => Domain.Investments.UnitType.Other,
        _ => Domain.Investments.UnitType.Unspecified
    };

    /// <summary>Maps the construction stage to its lowercase wire value.</summary>
    public static string ToWireConstructionStage(ConstructionStage stage) => stage switch
    {
        Domain.Investments.ConstructionStage.LandOnly => "land_only",
        Domain.Investments.ConstructionStage.Design => "design",
        Domain.Investments.ConstructionStage.UnderConstruction => "under_construction",
        Domain.Investments.ConstructionStage.Commissioned => "commissioned",
        _ => "unspecified"
    };

    /// <summary>
    /// Parses the wire construction stage back to the domain enum. Unknown / absent values map to
    /// <see cref="Domain.Investments.ConstructionStage.Unspecified"/>, which callers treat as
    /// "not stated" and <see cref="Property.SetConstructionProgress"/> treats as "leave as is".
    /// </summary>
    public static ConstructionStage ParseConstructionStage(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "land_only" => Domain.Investments.ConstructionStage.LandOnly,
        "design" => Domain.Investments.ConstructionStage.Design,
        "under_construction" => Domain.Investments.ConstructionStage.UnderConstruction,
        "commissioned" => Domain.Investments.ConstructionStage.Commissioned,
        _ => Domain.Investments.ConstructionStage.Unspecified
    };

    /// <summary>Maps the payout frequency to its lowercase wire value.</summary>
    public static string ToWirePayoutFrequency(PayoutFrequency frequency) => frequency switch
    {
        Domain.Investments.PayoutFrequency.None => "none",
        Domain.Investments.PayoutFrequency.Monthly => "monthly",
        Domain.Investments.PayoutFrequency.Quarterly => "quarterly",
        Domain.Investments.PayoutFrequency.Annually => "annually",
        _ => "unspecified"
    };

    /// <summary>
    /// Parses the wire payout frequency. Unknown / absent values map to
    /// <see cref="Domain.Investments.PayoutFrequency.Unspecified"/>, which
    /// <see cref="Property.SetPayoutFrequency"/> treats as "leave as is".
    /// </summary>
    public static PayoutFrequency ParsePayoutFrequency(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "none" => Domain.Investments.PayoutFrequency.None,
        "monthly" => Domain.Investments.PayoutFrequency.Monthly,
        "quarterly" => Domain.Investments.PayoutFrequency.Quarterly,
        "annually" => Domain.Investments.PayoutFrequency.Annually,
        _ => Domain.Investments.PayoutFrequency.Unspecified
    };
}
