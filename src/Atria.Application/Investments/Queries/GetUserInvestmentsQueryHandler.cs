using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Builds another investor's active portfolio for the admin/compliance investor card: one row per
/// property with the denormalized name, tokens held, invested amount, and the share of the property.
/// An investor with no active holdings yields an empty list (never a failure).
/// </summary>
public sealed class GetUserInvestmentsQueryHandler
    : IRequestHandler<GetUserInvestmentsQuery, Result<IReadOnlyList<UserInvestmentDto>>>
{
    private readonly IInvestmentRepository _investments;

    public GetUserInvestmentsQueryHandler(IInvestmentRepository investments)
        => _investments = investments;

    public async Task<Result<IReadOnlyList<UserInvestmentDto>>> Handle(
        GetUserInvestmentsQuery request, CancellationToken ct)
    {
        var holdings = await _investments.GetActiveHoldingsByInvestorAsync(request.InvestorId, ct);

        IReadOnlyList<UserInvestmentDto> dtos = holdings
            .Select(h => new UserInvestmentDto(
                h.PropertyId,
                h.PropertyName,
                h.TokenCount,
                h.Amount,
                h.Currency,
                SharePercent(h.TokenCount, h.TotalTokens),
                InvestmentStatus.Active))
            .ToList();

        return Result.Success(dtos);
    }

    // Investor's share of the property, rounded to 4 decimals. Guards against a zero supply (never
    // expected for a real property) so the read can't throw.
    private static decimal SharePercent(long tokenCount, long totalTokens)
        => totalTokens <= 0 ? 0m : Math.Round((decimal)tokenCount / totalTokens * 100m, 4);
}
