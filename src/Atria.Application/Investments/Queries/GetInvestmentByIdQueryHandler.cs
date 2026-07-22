using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Users;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Returns one investment. The owner may read their own; an Admin may read any.
/// Anyone else is told the row was not found (no existence leak).
/// </summary>
public sealed class GetInvestmentByIdQueryHandler
    : IRequestHandler<GetInvestmentByIdQuery, Result<InvestmentDto>>
{
    private readonly IInvestmentRepository _investments;
    private readonly ICurrentUserService _currentUser;

    public GetInvestmentByIdQueryHandler(IInvestmentRepository investments, ICurrentUserService currentUser)
    {
        _investments = investments;
        _currentUser = currentUser;
    }

    public async Task<Result<InvestmentDto>> Handle(GetInvestmentByIdQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<InvestmentDto>(
                Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investment = await _investments.GetByIdAsync(request.Id, ct);
        if (investment is null)
            return Result.Failure<InvestmentDto>(
                Error.NotFound("investment.notFound", "Investment not found."));

        // Resource-based authorization: owner or Admin only.
        var isOwner = investment.InvestorId == userId.Value;
        if (!isOwner && !_currentUser.IsInRole(Role.Admin))
            return Result.Failure<InvestmentDto>(
                Error.NotFound("investment.notFound", "Investment not found."));

        var dto = new InvestmentDto(
            investment.Id, investment.PropertyId, investment.TokenCount, investment.Amount,
            investment.Currency, investment.PricePerToken, investment.Status, investment.OnChainStatus,
            investment.TransactionHash, investment.CreatedAtUtc);

        return Result.Success(dto);
    }
}
