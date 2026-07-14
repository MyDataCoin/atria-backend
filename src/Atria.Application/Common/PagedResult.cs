namespace Atria.Application.Common;

/// <summary>A single page of results plus the totals a client needs to render pagination.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Page">1-based page number that was returned.</param>
/// <param name="PageSize">Maximum number of items per page.</param>
/// <param name="TotalCount">Total number of items matching the query across all pages.</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>Total number of pages for <see cref="TotalCount"/> at <see cref="PageSize"/>.</summary>
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
