using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// Applications across every investor — the operator's queue. Operator only: this is the one read that
/// crosses investor boundaries, so it never leaves the Admin surface.
/// </summary>
/// <param name="Status">Narrow to one lifecycle status; <c>null</c> returns all of them.</param>
/// <param name="PropertyId">Narrow to one issue; <c>null</c> returns applications across all issues.</param>
/// <param name="Take">How many to return, capped at 500.</param>
public sealed record ListInvestmentsQuery(InvestmentStatus? Status, Guid? PropertyId, int Take = 100)
    : IRequest<Result<IReadOnlyList<InvestmentDto>>>;

public sealed class ListInvestmentsQueryHandler
    : IRequestHandler<ListInvestmentsQuery, Result<IReadOnlyList<InvestmentDto>>>
{
    private readonly IInvestmentRepository _investments;

    public ListInvestmentsQueryHandler(IInvestmentRepository investments) => _investments = investments;

    public async Task<Result<IReadOnlyList<InvestmentDto>>> Handle(
        ListInvestmentsQuery request, CancellationToken ct)
    {
        var take = Math.Clamp(request.Take, 1, 500);
        var investments = await _investments.ListAsync(request.Status, request.PropertyId, take, ct);

        IReadOnlyList<InvestmentDto> dtos = investments
            .Select(i => new InvestmentDto(i.Id, i.PropertyId, i.InvestorId, i.TokenCount, i.Amount, i.Currency,
                i.PricePerToken, i.Status, i.ReservedUntilUtc, i.RejectionReason, i.OnChainStatus,
                i.TransactionHash, i.CreatedAtUtc))
            .ToList();

        return Result.Success(dtos);
    }
}
