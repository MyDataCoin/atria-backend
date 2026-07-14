using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Publications.Dtos;
using Atria.Domain.Publications;
using Atria.Domain.Users;

namespace Atria.Application.Publications.Queries;

/// <summary>
/// The news feed, newest first. Role-scoped: an Admin sees drafts too, everyone else (investor or
/// anonymous) sees only published items.
/// </summary>
/// <param name="PropertyId">Filter to one property's items.</param>
/// <param name="GeneralOnly">Only general items (no property attached).</param>
/// <param name="Type">Filter by wire type value (e.g. <c>news_release</c>).</param>
/// <param name="Page">1-based page number; defaults to 1.</param>
/// <param name="PageSize">Items per page; defaults to 20, capped at 100.</param>
public sealed record GetPublicationsQuery(
    Guid? PropertyId = null,
    bool GeneralOnly = false,
    string? Type = null,
    int? Page = null,
    int? PageSize = null) : IRequest<Result<PagedResult<PublicationDto>>>;

public sealed class GetPublicationsQueryHandler
    : IRequestHandler<GetPublicationsQuery, Result<PagedResult<PublicationDto>>>
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IPublicationRepository _publications;
    private readonly ICurrentUserService _currentUser;

    public GetPublicationsQueryHandler(
        IPublicationRepository publications, ICurrentUserService currentUser)
    {
        _publications = publications;
        _currentUser = currentUser;
    }

    public async Task<Result<PagedResult<PublicationDto>>> Handle(
        GetPublicationsQuery request, CancellationToken ct)
    {
        PublicationType? type = null;
        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            if (!PublicationDto.TryParseType(request.Type, out var parsed))
                return Result.Failure<PagedResult<PublicationDto>>(Error.Validation(
                    "publication.invalid_type",
                    "Type must be one of: financial_report, news_release, valuation_audit, general_news."));
            type = parsed;
        }

        // Drafts are admin-only; investors and anonymous callers see published items only.
        var publishedOnly = !_currentUser.IsInRole(Role.Admin);

        var page = Math.Max(1, request.Page ?? 1);
        var pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, 1, MaxPageSize);

        var filter = new PublicationFilter(request.PropertyId, request.GeneralOnly, type, publishedOnly);
        var (rows, totalCount) = await _publications.GetPageAsync(filter, page, pageSize, ct);

        IReadOnlyList<PublicationDto> items = rows
            .Select(r => PublicationDto.From(r.Publication, r.PropertyName))
            .ToList();

        return Result.Success(new PagedResult<PublicationDto>(items, page, pageSize, totalCount));
    }
}
