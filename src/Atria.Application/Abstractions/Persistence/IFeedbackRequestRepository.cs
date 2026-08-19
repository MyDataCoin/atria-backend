using Atria.Domain.Feedback;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for public-site <see cref="FeedbackRequest"/>s.</summary>
public interface IFeedbackRequestRepository : IRepository<FeedbackRequest>
{
    /// <summary>All requests, newest first. Admin/SuperAdmin reporting read.</summary>
    Task<IReadOnlyList<FeedbackRequest>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Deletes requests created before <paramref name="olderThanUtc"/> and returns the row count.
    /// This is what keeps the retention promise made in the consent text.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime olderThanUtc, CancellationToken ct);
}
