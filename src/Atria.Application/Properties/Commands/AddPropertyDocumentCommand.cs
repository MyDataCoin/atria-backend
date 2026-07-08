using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;

namespace Atria.Application.Properties.Commands;

/// <summary>Uploads a document for a property (Admin). No count limit.</summary>
public sealed record AddPropertyDocumentCommand(
    Guid PropertyId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes) : IRequest<Result<PropertyDocumentDto>>;

/// <summary>
/// Persists the file to media storage and records its URL + metadata on the property aggregate.
/// </summary>
public sealed class AddPropertyDocumentCommandHandler
    : IRequestHandler<AddPropertyDocumentCommand, Result<PropertyDocumentDto>>
{
    private const string Category = "documents";

    private readonly IPropertyRepository _properties;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public AddPropertyDocumentCommandHandler(
        IPropertyRepository properties, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PropertyDocumentDto>> Handle(AddPropertyDocumentCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<PropertyDocumentDto>(Error.NotFound("property.notFound", "Property not found."));

        var url = await _storage.SaveAsync(request.Content, request.FileName, request.ContentType, Category, ct);
        var document = property.AddDocument(url, request.FileName, request.ContentType);

        // property is tracked — the change tracker INSERTs the new child on save. Do NOT call
        // Update() (it would mark the new row Modified -> UPDATE of a missing row -> 0 rows).
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new PropertyDocumentDto(
            document.Id, document.Url, document.FileName, document.ContentType));
    }
}
