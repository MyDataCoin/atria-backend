using Atria.Application.Abstractions;
using Atria.Application.Buildings.Dtos;
using Atria.Application.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Buildings.Commands;

/// <summary>Uploads a photo for a building (Admin). Enforced max is <see cref="Building.MaxImages"/>.</summary>
public sealed record AddBuildingImageCommand(
    Guid BuildingId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes) : IRequest<Result<BuildingImageDto>>;

/// <summary>
/// Loads the building, checks the image limit BEFORE writing bytes (so a rejected upload never
/// leaves an orphan file), persists the file to media storage and records the URL on the aggregate.
/// </summary>
public sealed class AddBuildingImageCommandHandler
    : IRequestHandler<AddBuildingImageCommand, Result<BuildingImageDto>>
{
    private const string Category = "images";

    private readonly IBuildingRepository _buildings;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public AddBuildingImageCommandHandler(
        IBuildingRepository buildings, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BuildingImageDto>> Handle(AddBuildingImageCommand request, CancellationToken ct)
    {
        var building = await _buildings.GetByIdAsync(request.BuildingId, ct);
        if (building is null)
            return Result.Failure<BuildingImageDto>(Error.NotFound("building.notFound", "Building not found."));

        // Guard the limit before spending storage on an upload that would be rejected.
        if (building.Images.Count >= Building.MaxImages)
            return Result.Failure<BuildingImageDto>(Error.Conflict(
                "building.image_limit", $"A building can have at most {Building.MaxImages} images."));

        var url = await _storage.SaveAsync(request.Content, request.FileName, request.ContentType, Category, ct);
        var image = building.AddImage(url);

        // building is tracked — the change tracker INSERTs the new child. Do NOT call Update():
        // it marks the whole graph Modified, turning the INSERT into an UPDATE of a missing row.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new BuildingImageDto(image.Id, image.Url));
    }
}
