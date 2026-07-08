using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Removes a property document (Admin) and deletes its file from media storage.</summary>
public sealed record RemovePropertyDocumentCommand(Guid PropertyId, Guid DocumentId) : IRequest<Result>;

/// <summary>Removes the document from the aggregate, deletes the underlying file, and persists.</summary>
public sealed class RemovePropertyDocumentCommandHandler : IRequestHandler<RemovePropertyDocumentCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IMediaStorage _storage;
    private readonly IUnitOfWork _unitOfWork;

    public RemovePropertyDocumentCommandHandler(
        IPropertyRepository properties, IMediaStorage storage, IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _storage = storage;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemovePropertyDocumentCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        var removed = property.RemoveDocument(request.DocumentId);
        if (removed is null)
            return Result.Failure(Error.NotFound("property.document_notFound", "Document not found."));

        // property is tracked — removing the child marks it Deleted; the tracker DELETEs it on save.
        await _unitOfWork.SaveChangesAsync(ct);

        _storage.Delete(removed.Url);

        return Result.Success();
    }
}
