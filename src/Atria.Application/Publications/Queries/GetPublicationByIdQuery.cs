using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Publications.Dtos;
using Atria.Domain.Publications;
using Atria.Domain.Users;

namespace Atria.Application.Publications.Queries;

/// <summary>One publication with its full body. Drafts are visible to Admin only.</summary>
public sealed record GetPublicationByIdQuery(Guid Id) : IRequest<Result<PublicationDto>>;

public sealed class GetPublicationByIdQueryHandler
    : IRequestHandler<GetPublicationByIdQuery, Result<PublicationDto>>
{
    private readonly IPublicationRepository _publications;
    private readonly ICurrentUserService _currentUser;

    public GetPublicationByIdQueryHandler(
        IPublicationRepository publications, ICurrentUserService currentUser)
    {
        _publications = publications;
        _currentUser = currentUser;
    }

    public async Task<Result<PublicationDto>> Handle(GetPublicationByIdQuery request, CancellationToken ct)
    {
        var row = await _publications.GetByIdWithPropertyAsync(request.Id, ct);

        // A draft is reported as not found to non-admins so its existence is not leaked.
        var isAdmin = _currentUser.IsInRole(Role.Admin);
        if (row is null || (!isAdmin && row.Value.Publication.Status != PublicationStatus.Published))
            return Result.Failure<PublicationDto>(
                Error.NotFound("publication.not_found", "Publication not found."));

        return Result.Success(PublicationDto.From(row.Value.Publication, row.Value.PropertyName));
    }
}
