using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Edits a property's descriptive details. Admin only. Only non-null fields are applied, so a client
/// can PATCH one field at a time. Economics (value, token price/supply, currency) and the lifecycle
/// status are not editable here.
/// </summary>
/// <param name="Id">The property to edit.</param>
/// <param name="Name">New display name; <c>null</c> to leave unchanged.</param>
/// <param name="Description">New description; <c>null</c> to leave unchanged.</param>
/// <param name="Address">New address; <c>null</c> to leave unchanged.</param>
/// <param name="PropertyType">New kind; <c>null</c> to leave unchanged.</param>
/// <param name="City">New city; <c>null</c> to leave unchanged.</param>
/// <param name="YearBuilt">New build year; <c>null</c> to leave unchanged.</param>
/// <param name="Developer">New developer; <c>null</c> to leave unchanged.</param>
/// <param name="Floors">New storey count; <c>null</c> to leave unchanged.</param>
/// <param name="BuildingId">Move the unit into this building; <c>null</c> to leave unchanged, all-zero Guid to detach it.</param>
/// <param name="UnitType">New unit kind (<c>apartment</c>, <c>garage</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="UnitNumber">New flat / garage box number; <c>null</c> to leave unchanged.</param>
/// <param name="FloorNumber">New floor the unit is on; <c>null</c> to leave unchanged.</param>
/// <param name="RoomCount">New room count; <c>null</c> to leave unchanged.</param>
/// <param name="Section">Car-park section; <c>null</c> CLEARS it — see <see cref="Property.SetParkingAddress"/>.</param>
/// <param name="Row">Row within the section; <c>null</c> CLEARS it.</param>
/// <param name="Spot">The parking space's own number; <c>null</c> CLEARS it.</param>
/// <param name="TotalAreaSqM">New total area in m²; <c>null</c> to leave unchanged.</param>
/// <param name="LandAreaHectares">New land plot area in hectares; <c>null</c> to leave unchanged.</param>
/// <param name="LandPlotCode">New cadastre identification code for the plot; <c>null</c> to leave unchanged.</param>
/// <param name="CadastralNumber">New cadastral number of the built object; <c>null</c> to leave unchanged.</param>
/// <param name="ConstructionStage">New construction stage (<c>land_only</c>, <c>design</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="PlannedCompletionDate">New expected commissioning date; <c>null</c> to leave unchanged.</param>
/// <param name="ReadinessPercent">New reported readiness, 0–100; <c>null</c> to leave unchanged.</param>
/// <param name="IsFreeOfEncumbrances">
/// Result of a cadastre check for encumbrances and arrests. Applied only together with
/// <paramref name="EncumbranceCheckedAtUtc"/> — a verdict with no date is not evidence.
/// </param>
/// <param name="EncumbranceCheckedAtUtc">When that check was made.</param>
/// <param name="PayoutFrequency">New distribution frequency (<c>none</c>, <c>monthly</c>, …); <c>null</c> to leave unchanged.</param>
/// <param name="Rooms">
/// Replaces the whole room breakdown when supplied; <c>null</c> leaves it untouched and an empty
/// list clears it.
/// </param>
public sealed record UpdatePropertyCommand(
    Guid Id,
    string? Name,
    string? Description,
    string? Address,
    string? PropertyType,
    string? City,
    int? YearBuilt,
    string? Developer,
    int? Floors,
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
    int? ReadinessPercent = null,
    bool? IsFreeOfEncumbrances = null,
    DateTime? EncumbranceCheckedAtUtc = null,
    string? PayoutFrequency = null) : IRequest<Result>;

public sealed class UpdatePropertyCommandHandler : IRequestHandler<UpdatePropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IBuildingRepository _buildings;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePropertyCommandHandler(
        IPropertyRepository properties,
        IBuildingRepository buildings,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _buildings = buildings;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdatePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can edit properties."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        // The all-zero Guid is the explicit "detach" signal: null already means "leave unchanged",
        // so without it a unit could never be pulled back out of its building.
        if (request.BuildingId == Guid.Empty && request.BuildingId is not null)
        {
            property.AssignToBuilding(null);
        }
        else if (request.BuildingId is { } buildingId)
        {
            if (await _buildings.GetByIdAsync(buildingId, ct) is null)
                return Result.Failure(Error.NotFound("building.notFound", "Building not found."));
            property.AssignToBuilding(buildingId);
        }

        property.UpdateDetails(
            request.Name, request.Description, request.Address, request.PropertyType,
            request.City, request.YearBuilt, request.Developer, request.Floors);

        property.SetUnitDetails(
            PropertyDto.ParseUnitType(request.UnitType), request.UnitNumber, request.FloorNumber,
            request.RoomCount, request.TotalAreaSqM);

        property.SetCadastralDetails(
            request.LandPlotCode, request.CadastralNumber, request.LandAreaHectares);

        property.SetConstructionProgress(
            PropertyDto.ParseConstructionStage(request.ConstructionStage),
            request.PlannedCompletionDate, request.ReadinessPercent);

        property.SetPayoutFrequency(PropertyDto.ParsePayoutFrequency(request.PayoutFrequency));

        // Both halves or neither: recording a verdict without the date it was reached would put an
        // undated all-clear on the record, and an all-clear nobody can date is not one.
        if (request.IsFreeOfEncumbrances is { } isFree && request.EncumbranceCheckedAtUtc is { } checkedAt)
            property.RecordEncumbranceCheck(isFree, checkedAt);

        // Deliberately NOT the "only non-null is applied" rule the line above follows: the admin form
        // sends all three as null as soon as the unit stops being a garage or a parking space, and a
        // section left over from before that switch has to go with it.
        property.SetParkingAddress(request.Section, request.Row, request.Spot);

        // null = leave the breakdown alone; an empty list clears it.
        if (request.Rooms is not null)
            property.ReplaceRooms(request.Rooms.Select(r => (r.Name, r.AreaSqM)));

        // No Update() call: the aggregate is tracked (loaded via GetByIdAsync), so the change tracker
        // already has the edits. Set.Update() would mark the whole graph Modified and turn the INSERTs
        // for newly added rooms into UPDATEs of rows that do not exist yet.

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyUpdated,
            $"Изменён объект «{property.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
