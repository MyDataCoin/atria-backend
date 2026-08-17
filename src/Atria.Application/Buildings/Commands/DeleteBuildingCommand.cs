using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Commands;

/// <summary>Deletes an empty building. Admin only.</summary>
public sealed record DeleteBuildingCommand(Guid Id) : IRequest<Result>;

/// <summary>
/// Deletes the building and its photos. Refuses (409) while any unit is still registered inside it:
/// those units are live token issues with holders behind them, so they must be moved or removed
/// deliberately rather than swept away by deleting their container.
/// </summary>
public sealed class DeleteBuildingCommandHandler : IRequestHandler<DeleteBuildingCommand, Result>
{
    private readonly IBuildingRepository _buildings;
    private readonly IMediaStorage _storage;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteBuildingCommandHandler(
        IBuildingRepository buildings,
        IMediaStorage storage,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
        _storage = storage;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteBuildingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("building.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("building.forbidden", "Only administrators can delete buildings."));

        var building = await _buildings.GetByIdAsync(request.Id, ct);
        if (building is null)
            return Result.Failure(Error.NotFound("building.notFound", "Building not found."));

        if (await _buildings.HasUnitsAsync(building.Id, ct))
            return Result.Failure(Error.Conflict(
                "building.hasUnits",
                "Building still has units. Move or delete them before deleting the building."));

        var imageUrls = building.Images.Select(i => i.Url).ToList();

        _buildings.Remove(building);

        await _audit.WriteAsync(
            AuditEntities.Building, building.Id, AuditEvents.BuildingDeleted,
            $"Удалено здание «{building.Name}»", AuditSeverity.Warning, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        // Files go only after the rows are gone, so a failed commit never orphans a URL.
        foreach (var url in imageUrls)
            _storage.Delete(url);

        return Result.Success();
    }
}
