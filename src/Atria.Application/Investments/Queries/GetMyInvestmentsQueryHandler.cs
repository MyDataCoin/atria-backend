using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Returns the current investor's own investments only.</summary>
public sealed class GetMyInvestmentsQueryHandler
    : IRequestHandler<GetMyInvestmentsQuery, Result<IReadOnlyList<InvestmentDto>>>
{
    private readonly IInvestmentRepository _investments;
    private readonly ICurrentUserService _currentUser;

    public GetMyInvestmentsQueryHandler(IInvestmentRepository investments, ICurrentUserService currentUser)
    {
        _investments = investments;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<InvestmentDto>>> Handle(GetMyInvestmentsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<IReadOnlyList<InvestmentDto>>(
                Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investments = await _investments.GetByInvestorAsync(userId.Value, ct);

        IReadOnlyList<InvestmentDto> dtos = investments
            .Select(i => new InvestmentDto(i.Id, i.PropertyId, i.TokenCount, i.Amount, i.Currency, i.Status, i.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
