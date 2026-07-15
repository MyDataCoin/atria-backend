using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Lists an investor's Active holdings for the admin/compliance investor card. One row per property.
/// </summary>
/// <param name="InvestorId">Id of the investor whose portfolio is requested.</param>
public sealed record GetUserInvestmentsQuery(Guid InvestorId)
    : IRequest<Result<IReadOnlyList<UserInvestmentDto>>>;
