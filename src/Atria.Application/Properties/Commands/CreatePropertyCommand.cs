using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Creates a new tokenized property — either a standalone issue or one unit (apartment, garage)
/// inside a building. Admin only.
/// </summary>
/// <param name="BuildingId">Building to register the unit in; omit for a standalone issue.</param>
/// <param name="UnitType">What the unit is: <c>apartment</c> | <c>garage</c> | <c>parking_space</c> | <c>commercial</c> | <c>storage</c> | <c>land_plot</c> | <c>other</c>.</param>
/// <param name="UnitNumber">Flat / garage box number inside the building.</param>
/// <param name="FloorNumber">Floor the unit is on.</param>
/// <param name="RoomCount">How many rooms the unit is sold as (2-, 3-, 4-комнатная).</param>
/// <param name="Section">Car-park section a garage / parking space sits in (e.g. <c>B</c>); optional.</param>
/// <param name="Row">Row within the section (e.g. <c>12А</c>); optional.</param>
/// <param name="Spot">The parking space's own number (e.g. <c>125</c>); optional.</param>
/// <param name="TotalAreaSqM">Total floor area of the unit in m².</param>
/// <param name="Rooms">Room breakdown: name + area per row, in display order.</param>
/// <param name="MinPurchaseTokens">Fewest tokens one application may be for. Defaults to a single token.</param>
/// <param name="LandAreaHectares">Area of the land plot in hectares. Kept apart from <paramref name="TotalAreaSqM"/>, which is floor area.</param>
/// <param name="LandPlotCode">The plot's identification code in the state cadastre (e.g. <c>1-04-13-0033-0135</c>).</param>
/// <param name="CadastralNumber">Cadastral number of the built object, when there is one.</param>
/// <param name="ConstructionStage">How far along the object is: <c>land_only</c> | <c>design</c> | <c>under_construction</c> | <c>commissioned</c>.</param>
/// <param name="PlannedCompletionDate">Expected commissioning date.</param>
/// <param name="ReadinessPercent">Reported construction readiness, 0–100.</param>
public sealed record CreatePropertyCommand(
    string Name,
    string? Description,
    string? Address,
    decimal TotalValue,
    decimal TokenPrice,
    long TotalTokens,
    string Currency,
    long MinPurchaseTokens = TokenAmount.Smallest,
    string? PropertyType = null,
    string? City = null,
    int? YearBuilt = null,
    string? Developer = null,
    int? Floors = null,
    Guid? BuildingId = null,
    string? UnitType = null,
    string? UnitNumber = null,
    int? FloorNumber = null,
    int? RoomCount = null,
    string? Section = null,
    string? Row = null,
    string? Spot = null,
    decimal? TotalAreaSqM = null,
    IReadOnlyList<PropertyRoomInput>? Rooms = null,
    decimal? LandAreaHectares = null,
    string? LandPlotCode = null,
    string? CadastralNumber = null,
    string? ConstructionStage = null,
    DateTime? PlannedCompletionDate = null,
    int? ReadinessPercent = null) : IRequest<Result<Guid>>;
