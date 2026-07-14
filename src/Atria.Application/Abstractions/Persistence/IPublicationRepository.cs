using Atria.Domain.Publications;

namespace Atria.Application.Abstractions;

/// <summary>Filters for a news-feed query. All are optional and combine with AND.</summary>
/// <param name="PropertyId">Only items about this property.</param>
/// <param name="GeneralOnly">Only general items (no property attached).</param>
/// <param name="Type">Only items of this kind.</param>
/// <param name="PublishedOnly">Hide drafts (everyone except Admin).</param>
public sealed record PublicationFilter(
    Guid? PropertyId,
    bool GeneralOnly,
    PublicationType? Type,
    bool PublishedOnly);

/// <summary>
/// Aggregate repository for <see cref="Publication"/>. List reads return the page plus the total
/// count and denormalize each item's property name (LEFT JOIN) so the feed needs no second query.
/// </summary>
public interface IPublicationRepository : IRepository<Publication>
{
    /// <summary>
    /// One page of the feed, newest first (<c>PublishedAtUtc DESC</c>), with each item's property
    /// name (null for general items) and the total count across all pages.
    /// </summary>
    Task<(IReadOnlyList<(Publication Publication, string? PropertyName)> Items, int TotalCount)>
        GetPageAsync(PublicationFilter filter, int page, int pageSize, CancellationToken ct);

    /// <summary>One publication with its property name, or null when it does not exist.</summary>
    Task<(Publication Publication, string? PropertyName)?> GetByIdWithPropertyAsync(
        Guid id, CancellationToken ct);
}
