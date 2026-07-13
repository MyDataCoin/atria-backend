using Atria.Domain.Deals;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Deal"/> with realtor- and token-scoped lookups.</summary>
public interface IDealRepository : IRepository<Deal>
{
    /// <summary>Every deal owned by a realtor, newest first.</summary>
    Task<IReadOnlyList<Deal>> GetByRealtorAsync(Guid realtorId, CancellationToken ct);

    /// <summary>Finds a deal by its referral token, or null when none matches.</summary>
    Task<Deal?> GetByReferralTokenAsync(string referralToken, CancellationToken ct);

    /// <summary>Pending deals whose links expired at or before <paramref name="asOfUtc"/> (for the expiry sweep).</summary>
    Task<IReadOnlyList<Deal>> GetExpiredPendingAsync(DateTime asOfUtc, CancellationToken ct);
}
