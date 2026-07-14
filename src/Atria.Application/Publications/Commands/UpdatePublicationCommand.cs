using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Publications.Dtos;

namespace Atria.Application.Publications.Commands;

/// <summary>Edits a publication's copy (title / body / type). Admin only. Null fields stay unchanged.</summary>
public sealed record UpdatePublicationCommand(Guid Id, string? Type, string? Title, string? Body)
    : IRequest<Result<PublicationDto>>;

/// <summary>
/// Applies a copy edit to an existing item (e.g. fixing a typo in an already-sent report). The
/// property link, author and publication time are immutable, and no new reader notification is sent.
/// </summary>
public sealed class UpdatePublicationCommandHandler
    : IRequestHandler<UpdatePublicationCommand, Result<PublicationDto>>
{
    private readonly IPublicationRepository _publications;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePublicationCommandHandler(IPublicationRepository publications, IUnitOfWork unitOfWork)
    {
        _publications = publications;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PublicationDto>> Handle(UpdatePublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.Id, ct);
        if (publication is null)
            return Result.Failure<PublicationDto>(
                Error.NotFound("publication.not_found", "Publication not found."));

        Domain.Publications.PublicationType? type = null;
        if (request.Type is not null)
        {
            if (!PublicationDto.TryParseType(request.Type, out var parsed))
                return Result.Failure<PublicationDto>(Error.Validation(
                    "publication.invalid_type",
                    "Type must be one of: financial_report, news_release, valuation_audit, general_news."));
            type = parsed;
        }

        publication.Update(type, request.Title, request.Body);

        _publications.Update(publication);
        await _unitOfWork.SaveChangesAsync(ct);

        // Re-read the joined property name so the response carries the same shape as the feed.
        var row = await _publications.GetByIdWithPropertyAsync(publication.Id, ct);
        return Result.Success(PublicationDto.From(publication, row?.PropertyName));
    }
}
