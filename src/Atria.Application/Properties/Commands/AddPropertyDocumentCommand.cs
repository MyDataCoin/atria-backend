using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Properties.Commands;

/// <summary>Uploads a document for a property (Admin). No count limit.</summary>
/// <param name="PropertyId">The property the document belongs to.</param>
/// <param name="Content">The file's bytes.</param>
/// <param name="FileName">Name of the uploaded file.</param>
/// <param name="ContentType">MIME type of the file.</param>
/// <param name="SizeBytes">Size of the upload.</param>
/// <param name="DocumentCategory">
/// What the document is: <c>legal</c> | <c>technical_passport</c> | <c>valuation</c> |
/// <c>collateral</c> | <c>construction_schedule</c> | <c>layout</c>; omit when not stated.
/// </param>
/// <param name="Title">What to call it in a list; falls back to <paramref name="FileName"/>.</param>
public sealed record AddPropertyDocumentCommand(
    Guid PropertyId,
    Stream Content,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? DocumentCategory = null,
    string? Title = null) : IRequest<Result<PropertyDocumentDto>>;

/// <summary>
/// Persists the file to media storage and records its URL + metadata on the property aggregate.
/// </summary>
public sealed class AddPropertyDocumentCommandHandler
    : IRequestHandler<AddPropertyDocumentCommand, Result<PropertyDocumentDto>>
{
    /// <summary>Storage folder the bytes go into — unrelated to the document's category.</summary>
    private const string StorageFolder = "documents";

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

        var url = await _storage.SaveAsync(
            request.Content, request.FileName, request.ContentType, StorageFolder, ct);

        PropertyDocument document;
        try
        {
            document = property.AddDocument(
                url, request.FileName, request.ContentType,
                PropertyDocumentDto.ParseCategory(request.DocumentCategory), request.Title);
        }
        catch (DomainException ex)
        {
            return Result.Failure<PropertyDocumentDto>(Error.Validation("document.invalid", ex.Message));
        }

        // property is tracked — the change tracker INSERTs the new child on save. Do NOT call
        // Update() (it would mark the new row Modified -> UPDATE of a missing row -> 0 rows).
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PropertyDocumentDto.From(document));
    }
}
