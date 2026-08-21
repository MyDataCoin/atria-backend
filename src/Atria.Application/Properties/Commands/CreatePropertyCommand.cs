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
/// <param name="UnitType">What the unit is: <c>apartment</c> | <c>garage</c> | <c>parking_space</c> | <c>commercial</c> | <c>storage</c> | <c>other</c>.</param>
/// <param name="UnitNumber">Flat / garage box number inside the building.</param>
/// <param name="FloorNumber">Floor the unit is on.</param>
/// <param name="RoomCount">How many rooms the unit is sold as (2-, 3-, 4-комнатная).</param>
/// <param name="TotalAreaSqM">Total floor area of the unit in m².</param>
/// <param name="Rooms">Room breakdown: name + area per row, in display order.</param>
/// <param name="MinPurchaseTokens">Fewest tokens one application may be for. Defaults to a single token.</param>
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
    decimal? TotalAreaSqM = null,
    IReadOnlyList<PropertyRoomInput>? Rooms = null) : IRequest<Result<Guid>>;
