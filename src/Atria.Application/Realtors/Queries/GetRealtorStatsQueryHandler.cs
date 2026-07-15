using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Realtors.Dtos;

namespace Atria.Application.Realtors.Queries;

/// <summary>
/// Builds the admin realtor leaderboard: every realtor with their closed (Successful) and total deal
/// counts, ranked DB-side by closed deals. No realtors yields an empty list (a success, never a 404).
/// </summary>
public sealed class GetRealtorStatsQueryHandler
    : IRequestHandler<GetRealtorStatsQuery, Result<IReadOnlyList<RealtorStatsDto>>>
{
    private readonly IRealtorProfileRepository _realtors;

    public GetRealtorStatsQueryHandler(IRealtorProfileRepository realtors)
        => _realtors = realtors;

    public async Task<Result<IReadOnlyList<RealtorStatsDto>>> Handle(
        GetRealtorStatsQuery request, CancellationToken ct)
    {
        var stats = await _realtors.GetStatsAsync(ct);

        IReadOnlyList<RealtorStatsDto> dtos = stats
            .Select(s => new RealtorStatsDto(s.UserId, s.FullName, s.CompanyName, s.ClosedDeals, s.TotalDeals, s.Blocked))
            .ToList();

        return Result.Success(dtos);
    }
}
