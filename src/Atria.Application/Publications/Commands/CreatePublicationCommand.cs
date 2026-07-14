using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Publications.Dtos;
using Atria.Domain.Publications;

namespace Atria.Application.Publications.Commands;

/// <summary>Creates and publishes a news-feed item. Admin only.</summary>
/// <param name="Type">Wire type value (e.g. <c>financial_report</c>).</param>
/// <param name="Title">Headline.</param>
/// <param name="Body">Plain-text body.</param>
/// <param name="PropertyId">Property the item is about; null for general platform news.</param>
public sealed record CreatePublicationCommand(string Type, string Title, string Body, Guid? PropertyId)
    : IRequest<Result<PublicationDto>>;

/// <summary>
/// Validates the wire type and (when supplied) the target property, then publishes the item
/// authored by the current admin. Publishing raises a domain event that notifies readers.
/// </summary>
public sealed class CreatePublicationCommandHandler
    : IRequestHandler<CreatePublicationCommand, Result<PublicationDto>>
{
    private readonly IPublicationRepository _publications;
    private readonly IPropertyRepository _properties;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreatePublicationCommandHandler(
        IPublicationRepository publications,
        IPropertyRepository properties,
        IAuditWriter audit,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _publications = publications;
        _properties = properties;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<PublicationDto>> Handle(CreatePublicationCommand request, CancellationToken ct)
    {
        var authorId = _currentUser.UserId;
        if (authorId is null)
            return Result.Failure<PublicationDto>(
                Error.Unauthorized("publication.unauthorized", "Authentication required."));

        if (!PublicationDto.TryParseType(request.Type, out var type))
            return Result.Failure<PublicationDto>(Error.Validation(
                "publication.invalid_type",
                "Type must be one of: financial_report, news_release, valuation_audit, general_news."));

        // A property-scoped item must point at a property that actually exists; a null id is a
        // general platform news item and needs no lookup.
        string? propertyName = null;
        if (request.PropertyId is { } propertyId)
        {
            var property = await _properties.GetByIdAsync(propertyId, ct);
            if (property is null)
                return Result.Failure<PublicationDto>(
                    Error.NotFound("publication.property_not_found", "Property not found."));
            propertyName = property.Name;
        }

        var publication = Publication.Publish(
            type, request.Title, request.Body, request.PropertyId, authorId.Value, _clock.UtcNow);

        await _publications.AddAsync(publication, ct);

        var target = propertyName is null ? "общая новость" : $"объект «{propertyName}»";
        await _audit.WriteAsync(
            Audit.AuditEntities.Publication, publication.Id, Audit.AuditEvents.PublicationPublished,
            $"Опубликовано «{publication.Title}» ({target})",
            Domain.Audit.AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PublicationDto.From(publication, propertyName));
    }
}
