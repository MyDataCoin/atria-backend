using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Removes a property photo (Admin) and deletes its file from media storage.</summary>
public sealed record RemovePropertyImageCommand(Guid PropertyId, Guid ImageId) : IRequest<Result>;

/// <summary>
/// Removes the image from the aggregate, deletes the underlying file, and persists. A missing
/// property or image is reported as not found.
/// </summary>
public sealed class RemovePropertyImageCommandHandler : IRequestHandler<RemovePropertyImageCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public RemovePropertyImageCommandHandler(
        IPropertyRepository properties, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemovePropertyImageCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        var removed = property.RemoveImage(request.ImageId);
        if (removed is null)
            return Result.Failure(Error.NotFound("property.image_notFound", "Image not found."));

        // property is tracked — removing the child marks it Deleted; the tracker DELETEs it on save.
        await _unitOfWork.SaveChangesAsync(ct);

        // Delete the file only after the DB row is gone, so a failed commit never orphans the URL.
        _storage.Delete(removed.Url);

        return Result.Success();
    }
}
