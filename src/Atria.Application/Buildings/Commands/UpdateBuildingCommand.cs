using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Commands;

/// <summary>
/// Edits a building's details. Admin only. Only non-null fields are applied, so a client can PATCH
/// one field at a time.
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
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateBuildingCommandHandler(
        IBuildingRepository buildings,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
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

        await _audit.WriteAsync(
            AuditEntities.Building, building.Id, AuditEvents.BuildingUpdated,
            $"Изменено здание «{building.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
