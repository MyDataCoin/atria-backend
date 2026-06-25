using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Documents.Dtos;

namespace Atria.Application.Documents.Queries;

/// <summary>Lists the current user's documents.</summary>
public sealed record GetMyDocumentsQuery : IRequest<Result<IReadOnlyList<DocumentDto>>>;

/// <summary>Returns the documents owned by the authenticated user.</summary>
public sealed class GetMyDocumentsQueryHandler
    : IRequestHandler<GetMyDocumentsQuery, Result<IReadOnlyList<DocumentDto>>>
{
    private readonly IDocumentRepository _documents;
    private readonly ICurrentUserService _currentUser;

    public GetMyDocumentsQueryHandler(IDocumentRepository documents, ICurrentUserService currentUser)
    {
        _documents = documents;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<DocumentDto>>> Handle(
        GetMyDocumentsQuery request, CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
            return Result.Failure<IReadOnlyList<DocumentDto>>(
                Error.Unauthorized("auth.required", "Authentication is required."));

        var records = await _documents.GetByOwnerAsync(userId, ct);

        IReadOnlyList<DocumentDto> dtos = records
            .Select(d => new DocumentDto(d.Id, d.Type, d.FileName, d.ContentType, d.SizeBytes, d.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
