using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Properties.Commands;

/// <summary>Uploads an image for a property (Admin). Enforced max is <see cref="Property.MaxImages"/>.</summary>
/// <param name="Kind">
/// What the image is: <c>photo</c> | <c>render</c> | <c>floor_plan</c> | <c>site_plan</c>. Absent or
/// unrecognised means a photo. A render of an unbuilt object has to say so — see
/// <see cref="PropertyImageKind"/>.
/// </param>
/// <param name="Caption">Optional caption shown under the image.</param>
public sealed record AddPropertyImageCommand(
    Guid PropertyId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Kind = null,
    string? Caption = null) : IRequest<Result<PropertyImageDto>>;

/// <summary>
/// Loads the property, checks the image limit BEFORE writing bytes (so a rejected upload never
/// leaves an orphan file), persists the file to media storage, records the URL on the aggregate,
/// and returns the new image. Storage saves under a UUID name and returns the public URL.
/// </summary>
public sealed class AddPropertyImageCommandHandler
    : IRequestHandler<AddPropertyImageCommand, Result<PropertyImageDto>>
{
    private const string Category = "images";

    private readonly IPropertyRepository _properties;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public AddPropertyImageCommandHandler(
        IPropertyRepository properties, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PropertyImageDto>> Handle(AddPropertyImageCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<PropertyImageDto>(Error.NotFound("property.notFound", "Property not found."));

        // Guard the limit before spending storage on an upload that would be rejected.
        if (property.Images.Count >= Property.MaxImages)
            return Result.Failure<PropertyImageDto>(Error.Conflict(
                "property.image_limit", $"A property can have at most {Property.MaxImages} images."));

        var url = await _storage.SaveAsync(request.Content, request.FileName, request.ContentType, Category, ct);

        PropertyImage image;
        try
        {
            image = property.AddImage(
                url, PropertyImageDto.ParseKind(request.Kind), request.Caption);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PropertyImageDto>(Error.Validation("property.image_invalid", ex.Message));
        }

        // property is tracked (loaded via GetByIdAsync) — the change tracker INSERTs the new child.
        // Do NOT call Update(): it marks the whole graph Modified, turning the INSERT into an UPDATE
        // of a non-existent row (0 rows affected -> DbUpdateConcurrencyException).
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PropertyImageDto.From(image));
    }
}
