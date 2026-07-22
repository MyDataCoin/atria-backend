using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Builds the current investor's portfolio: total invested across Active investments,
/// the active count, and the per-investment breakdown.
/// </summary>
public sealed class GetPortfolioQueryHandler
    : IRequestHandler<GetPortfolioQuery, Result<PortfolioDto>>
{
    private readonly IInvestmentRepository _investments;
    private readonly ICurrentUserService _currentUser;

    public GetPortfolioQueryHandler(IInvestmentRepository investments, ICurrentUserService currentUser)
    {
        _investments = investments;
        _currentUser = currentUser;
    }

    public async Task<Result<PortfolioDto>> Handle(GetPortfolioQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<PortfolioDto>(
                Error.Unauthorized("portfolio.unauthorized", "Authentication is required."));

        var investments = await _investments.GetByInvestorAsync(userId.Value, ct);

        var dtos = investments
            .Select(i => new InvestmentDto(i.Id, i.PropertyId, i.TokenCount, i.Amount, i.Currency,
                i.PricePerToken, i.Status, i.OnChainStatus, i.TransactionHash, i.CreatedAtUtc))
            .ToList();

        // Only confirmed (Active) investments count toward invested capital; aggregated DB-side.
        var (totalInvested, activeCount) = await _investments.GetActiveTotalsAsync(userId.Value, ct);

        var portfolio = new PortfolioDto(totalInvested, activeCount, dtos);
        return Result.Success(portfolio);
    }
}
