using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Sets the gallery order of a property's images. Admin only. The first id becomes the cover.
/// </summary>
/// <param name="PropertyId">The property.</param>
/// <param name="ImageIds">Every image of the property, exactly once, in the order to display them.</param>
public sealed record ReorderPropertyImagesCommand(
    Guid PropertyId,
    IReadOnlyList<Guid> ImageIds) : IRequest<Result>;

/// <summary>
/// Reorders the gallery. Which image is first is a real decision — the management company was told
/// the first one is the cover — and before this it was whatever order the uploads happened to come
/// back in.
/// </summary>
public sealed class ReorderPropertyImagesCommandHandler
    : IRequestHandler<ReorderPropertyImagesCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _uow;

    public ReorderPropertyImagesCommandHandler(
        IPropertyRepository properties, ICurrentUserService currentUser, IUnitOfWork uow)
    {
        _properties = properties;
        _currentUser = currentUser;
        _uow = uow;
    }

    public async Task<Result> Handle(ReorderPropertyImagesCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("property.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(Error.Forbidden("property.forbidden", "Only administrators can reorder images."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        try
        {
            property.ReorderImages(request.ImageIds);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("property.image_order_invalid", ex.Message));
        }

        // No Update(): the aggregate is tracked, so the change tracker already has the moves.
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
