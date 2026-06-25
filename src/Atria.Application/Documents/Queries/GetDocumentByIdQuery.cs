using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Documents.Dtos;
using Atria.Domain.Users;

namespace Atria.Application.Documents.Queries;

/// <summary>Downloads a single document (owner, or Admin/Compliance staff).</summary>
public sealed record GetDocumentByIdQuery(Guid Id) : IRequest<Result<DocumentDownloadDto>>;

/// <summary>
/// Loads document metadata, authorizes the caller (owner OR Admin/Compliance),
/// then streams the bytes back from object storage.
/// </summary>
public sealed class GetDocumentByIdQueryHandler
    : IRequestHandler<GetDocumentByIdQuery, Result<DocumentDownloadDto>>
{
    private readonly IDocumentRepository _documents;
    private readonly IDocumentStorage _storage;
    private readonly ICurrentUserService _currentUser;

    public GetDocumentByIdQueryHandler(
        IDocumentRepository documents,
        IDocumentStorage storage,
        ICurrentUserService currentUser)
    {
        _documents = documents;
        _storage = storage;
        _currentUser = currentUser;
    }

    public async Task<Result<DocumentDownloadDto>> Handle(
        GetDocumentByIdQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<DocumentDownloadDto>(
                Error.Unauthorized("auth.required", "Authentication is required."));

        var record = await _documents.GetByIdAsync(request.Id, ct);
        if (record is null)
            return Result.Failure<DocumentDownloadDto>(
                Error.NotFound("document.not_found", "Document not found."));

        // Resource-based authorization: owner, or staff who may view any document.
        var isStaff = _currentUser.IsInRole(Role.Admin) || _currentUser.IsInRole(Role.Compliance);
        if (record.OwnerUserId != userId && !isStaff)
            return Result.Failure<DocumentDownloadDto>(
                Error.Forbidden("document.forbidden", "You may not access this document."));

        var content = await _storage.GetAsync(record.StorageKey, ct);

        return Result.Success(new DocumentDownloadDto(content, record.FileName, record.ContentType));
    }
}
