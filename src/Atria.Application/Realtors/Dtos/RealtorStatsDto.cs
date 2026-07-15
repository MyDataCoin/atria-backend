namespace Atria.Application.Realtors.Dtos;

/// <summary>
/// One realtor's row in the admin/compliance realtor leaderboard: identity plus their deal counts.
/// The client derives the tier (Top / Professional / Newcomer) from <paramref name="ClosedDeals"/>,
/// so the backend returns only the numbers.
/// </summary>
/// <param name="Id">The realtor's user id.</param>
/// <param name="FullName">Realtor full name, from <c>realtor_profiles</c>.</param>
/// <param name="CompanyName">Registered company name (optional), from <c>realtor_profiles</c>.</param>
/// <param name="ClosedDeals">Number of deals settled <c>Successful</c> — the referral went through and activated. Ranking key.</param>
/// <param name="TotalDeals">All of the realtor's deals (pending + successful + rejected).</param>
/// <param name="Blocked">Whether the realtor's account is banned by a super admin.</param>
public sealed record RealtorStatsDto(
    Guid Id,
    string FullName,
    string? CompanyName,
    int ClosedDeals,
    int TotalDeals,
    bool Blocked);
