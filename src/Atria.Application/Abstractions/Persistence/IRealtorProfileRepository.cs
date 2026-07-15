using Atria.Domain.Realtors;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="RealtorProfile"/> (one profile per user).</summary>
public interface IRealtorProfileRepository : IRepository<RealtorProfile>
{
    Task<RealtorProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Every realtor profile with their deal counts for the admin leaderboard: closed (Successful) and
    /// total deals, one row per realtor. Realtors with no deals appear with zero counts. Admin/Compliance
    /// reporting read. Ordered by closed deals, then total, descending.
    /// </summary>
    Task<IReadOnlyList<(Guid UserId, string FullName, string? CompanyName, int ClosedDeals, int TotalDeals)>>
        GetStatsAsync(CancellationToken ct);
}
