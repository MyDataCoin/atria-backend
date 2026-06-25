using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Documents;

namespace Atria.Application.Documents.Commands;

/// <summary>Uploads a document for the current user; returns the new record id.</summary>
public sealed record UploadDocumentCommand(
    Stream Content,
    string FileName,
    string ContentType,
    DocumentType Type) : IRequest<Result<Guid>>;

/// <summary>
/// Persists the bytes to object storage, then stores a <see cref="DocumentRecord"/>
/// owned by the current user. Content size/type are checked by the validator.
/// </summary>
public sealed class UploadDocumentCommandHandler
    : IRequestHandler<UploadDocumentCommand, Result<Guid>>
{
    private readonly IDocumentRepository _documents;
    private readonly IDocumentStorage _storage;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UploadDocumentCommandHandler(
        IDocumentRepository documents,
        IDocumentStorage storage,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _documents = documents;
        _storage = storage;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(UploadDocumentCommand request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } ownerUserId)
            return Result.Failure<Guid>(Error.Unauthorized("auth.required", "Authentication is required."));

        // Capture size up front; the storage adapter consumes the stream.
        var sizeBytes = request.Content.CanSeek ? request.Content.Length : 0L;

        var storageKey = await _storage.SaveAsync(
            request.Content, request.FileName, request.ContentType, ct);

        var record = DocumentRecord.Create(
            ownerUserId, request.Type, request.FileName, request.ContentType, storageKey, sizeBytes);

        await _documents.AddAsync(record, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(record.Id);
    }
}
