using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Commands;

/// <summary>
/// Edits a building's details. Admin only, and only while every unit is a draft or coming soon.
/// Only non-null fields are applied, so a client can PATCH one field at a time. Values the units
/// inherited from the building follow the edit; values set on a unit itself are left alone.
/// </summary>
public sealed record UpdateBuildingCommand(
    Guid Id,
    string? Name = null,
    string? Description = null,
    string? Address = null,
    string? City = null,
    string? Developer = null,
    int? YearBuilt = null,
    int? Floors = null,
    string? BuildingType = null,
    decimal? TotalAreaSqM = null,
    string? ConstructionStage = null,
    DateTime? PlannedCompletionDate = null,
    string? PlannedCompletionPrecision = null,
    bool? HasElevator = null,
    bool? HasHeating = null,
    bool? HasSecurity = null,
    bool? HasParking = null,
    bool? IsFreeOfEncumbrances = null,
    DateTime? EncumbranceCheckedAtUtc = null) : IRequest<Result>;

public sealed class UpdateBuildingCommandHandler : IRequestHandler<UpdateBuildingCommand, Result>
{
    private readonly IBuildingRepository _buildings;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBuildingCommandHandler(
        IBuildingRepository buildings,
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
        _properties = properties;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateBuildingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("building.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("building.forbidden", "Only administrators can edit buildings."));

        var building = await _buildings.GetByIdAsync(request.Id, ct);
        if (building is null)
            return Result.Failure(Error.NotFound("building.notFound", "Building not found."));

        // Tracked, one by one: the repository's by-building read is no-tracking, and the units are
        // edited below alongside the building in the same save.
        var unitIds = (await _properties.GetByBuildingAsync(building.Id, ct)).Select(p => p.Id).ToList();
        var units = new List<Property>(unitIds.Count);
        foreach (var unitId in unitIds)
            if (await _properties.GetByIdAsync(unitId, ct) is { } unit)
                units.Add(unit);

        if (!Building.IsEditableWith(units.Select(u => u.Status)))
            return Result.Failure(Error.Conflict(
                "building.locked",
                "The building can only be edited while all its units are drafts or coming soon."));

        // What the units could have inherited — captured before the building changes, so a unit
        // whose value still equals it is known to carry the building's, not its own.
        var before = Snapshot.Of(building);

        building.UpdateDetails(
            request.Name, request.Description, request.Address, request.City,
            request.Developer, request.YearBuilt, request.Floors, request.BuildingType);

        building.SetCharacteristics(
            request.TotalAreaSqM, request.HasElevator, request.HasHeating,
            request.HasSecurity, request.HasParking);
        building.SetConstructionProgress(
            PropertyDto.ParseConstructionStage(request.ConstructionStage),
            request.PlannedCompletionDate,
            PropertyDto.ParseCompletionDatePrecision(request.PlannedCompletionPrecision));

        // Both halves or neither: an undated all-clear is not evidence of anything.
        if (request.IsFreeOfEncumbrances is { } isFree && request.EncumbranceCheckedAtUtc is { } checkedAt)
            building.RecordEncumbranceCheck(isFree, checkedAt);

        var after = Snapshot.Of(building);
        foreach (var unit in units)
            Reinherit(unit, before, after);

        await _audit.WriteAsync(
            AuditEntities.Building, building.Id, AuditEvents.BuildingUpdated,
            $"Изменено здание «{building.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    /// <summary>
    /// Hands the building's new values down to a unit — but only where the unit still carries the
    /// building's OLD value, i.e. inherited it. A value the admin set on the unit itself wins, the
    /// same rule the create form applies. The showcases read these fields from the unit, so without
    /// this an edit of the building would never reach an investor.
    /// </summary>
    private static void Reinherit(Property unit, Snapshot before, Snapshot after)
    {
        unit.UpdateDetails(
            address: Changed(unit.Address, before.Address, after.Address),
            city: Changed(unit.City, before.City, after.City),
            developer: Changed(unit.Developer, before.Developer, after.Developer),
            yearBuilt: unit.YearBuilt == before.YearBuilt && after.YearBuilt != before.YearBuilt ? after.YearBuilt : null,
            floors: unit.Floors == before.Floors && after.Floors != before.Floors ? after.Floors : null);

        if (unit.ConstructionStage == before.Stage
            && unit.PlannedCompletionDate == before.PlannedCompletionDate
            && unit.PlannedCompletionPrecision == before.Precision)
            unit.SetConstructionProgress(after.Stage, after.PlannedCompletionDate, plannedCompletionPrecision: after.Precision);

        if (unit.IsFreeOfEncumbrances == before.IsFree && unit.EncumbranceCheckedAtUtc == before.CheckedAt
            && after.IsFree is { } free && after.CheckedAt is { } at)
            unit.RecordEncumbranceCheck(free, at);

        unit.ReplaceAmenities(
            Amenity(unit.Elevator, before.Elevator, after.Elevator),
            Amenity(unit.Heating, before.Heating, after.Heating),
            Amenity(unit.Security, before.Security, after.Security),
            Amenity(unit.Parking, before.Parking, after.Parking));

        static string? Changed(string? unitValue, string? old, string? @new)
            => unitValue == old && @new != old ? @new : null;

        // Inherited (or never set) follows the building; anything else is the unit's own text.
        static string? Amenity(string? unitValue, string? old, string? @new)
            => unitValue is null || unitValue == old ? @new : unitValue;
    }

    private sealed record Snapshot(
        string? Address, string? City, string? Developer, int? YearBuilt, int? Floors,
        ConstructionStage Stage, DateTime? PlannedCompletionDate, CompletionDatePrecision Precision,
        bool? IsFree, DateTime? CheckedAt,
        string? Elevator, string? Heating, string? Security, string? Parking)
    {
        public static Snapshot Of(Building b) => new(
            b.Address, b.City, b.Developer, b.YearBuilt, b.Floors,
            b.ConstructionStage, b.PlannedCompletionDate, b.PlannedCompletionPrecision,
            b.IsFreeOfEncumbrances, b.EncumbranceCheckedAtUtc,
            b.AmenityText(b.HasElevator), b.AmenityText(b.HasHeating),
            b.AmenityText(b.HasSecurity), b.AmenityText(b.HasParking));
    }
}
