using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Commands;

/// <summary>
/// Registers a building — the container an admin fills with units (apartments, garages) afterwards.
/// Admin only. No tokens are issued here; that happens per unit.
/// </summary>
public sealed record CreateBuildingCommand(
    string Name,
    string? Description = null,
    string? Address = null,
    string? City = null,
    string? Developer = null,
    int? YearBuilt = null,
    int? Floors = null,
    string? BuildingType = null) : IRequest<Result<Guid>>;

public sealed class CreateBuildingCommandHandler : IRequestHandler<CreateBuildingCommand, Result<Guid>>
{
    private readonly IBuildingRepository _buildings;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CreateBuildingCommandHandler(
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

    public async Task<Result<Guid>> Handle(CreateBuildingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<Guid>(
                Error.Unauthorized("building.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure<Guid>(
                Error.Forbidden("building.forbidden", "Only administrators can create buildings."));

        var building = Building.Create(
            request.Name, request.Description, request.Address, request.City,
            request.Developer, request.YearBuilt, request.Floors, request.BuildingType);

        await _buildings.AddAsync(building, ct);

        // Audited in the SAME transaction as the write: the building can never appear without its entry.
        await _audit.WriteAsync(
            AuditEntities.Building, building.Id, AuditEvents.BuildingCreated,
            $"Создано здание «{building.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(building.Id);
    }
}
