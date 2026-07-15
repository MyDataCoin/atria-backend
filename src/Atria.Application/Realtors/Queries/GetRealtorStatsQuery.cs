using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Realtors.Dtos;

namespace Atria.Application.Realtors.Queries;

/// <summary>
/// Realtor leaderboard for the admin dashboard: one row per realtor with closed and total deal counts,
/// ranked by closed deals. Admin / Compliance.
/// </summary>
public sealed record GetRealtorStatsQuery
    : IRequest<Result<IReadOnlyList<RealtorStatsDto>>>;
