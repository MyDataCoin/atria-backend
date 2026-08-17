using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Buildings.Commands;

/// <summary>Removes a building photo (Admin) and deletes its file from media storage.</summary>
public sealed record RemoveBuildingImageCommand(Guid BuildingId, Guid ImageId) : IRequest<Result>;

/// <summary>
/// Removes the image from the aggregate, deletes the underlying file, and persists. A missing
/// building or image is reported as not found.
/// </summary>
public sealed class RemoveBuildingImageCommandHandler : IRequestHandler<RemoveBuildingImageCommand, Result>
{
    private readonly IBuildingRepository _buildings;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public RemoveBuildingImageCommandHandler(
        IBuildingRepository buildings, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveBuildingImageCommand request, CancellationToken ct)
    {
        var building = await _buildings.GetByIdAsync(request.BuildingId, ct);
        if (building is null)
            return Result.Failure(Error.NotFound("building.notFound", "Building not found."));

        var removed = building.RemoveImage(request.ImageId);
        if (removed is null)
            return Result.Failure(Error.NotFound("building.image_notFound", "Image not found."));

        // building is tracked — removing the child marks it Deleted; the tracker DELETEs it on save.
        await _unitOfWork.SaveChangesAsync(ct);

        // Delete the file only after the DB row is gone, so a failed commit never orphans the URL.
        _storage.Delete(removed.Url);

        return Result.Success();
    }
}
