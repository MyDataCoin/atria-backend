using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Deals.Commands;
using Atria.Application.Deals.Dtos;

namespace Atria.Application.Deals.Queries;

/// <summary>Lists every referral deal owned by the current realtor.</summary>
public sealed record GetMyDealsQuery : IRequest<Result<IReadOnlyList<DealDto>>>;

public sealed class GetMyDealsQueryHandler
    : IRequestHandler<GetMyDealsQuery, Result<IReadOnlyList<DealDto>>>
{
    private readonly IDealRepository _deals;
    private readonly IInvestmentRepository _investments;
    private readonly ICurrentUserService _currentUser;
    private readonly IReferralLinkBuilder _links;

    public GetMyDealsQueryHandler(
        IDealRepository deals,
        IInvestmentRepository investments,
        ICurrentUserService currentUser,
        IReferralLinkBuilder links)
    {
        _deals = deals;
        _investments = investments;
        _currentUser = currentUser;
        _links = links;
    }

    public async Task<Result<IReadOnlyList<DealDto>>> Handle(GetMyDealsQuery request, CancellationToken ct)
    {
        var realtorId = _currentUser.UserId;
        if (realtorId is null)
            return Result.Failure<IReadOnlyList<DealDto>>(
                Error.Unauthorized("deal.unauthorized", "Authentication required."));

        var deals = await _deals.GetByRealtorAsync(realtorId.Value, ct);

        // For successful deals, resolve the matched investment (amount + currency) in one batched
        // query so the realtor's earnings can be shown; pending/rejected deals carry no amount.
        var matchedIds = deals
            .Where(d => d.MatchedInvestmentId is not null)
            .Select(d => d.MatchedInvestmentId!.Value)
            .ToList();
        var investments = (await _investments.GetByIdsAsync(matchedIds, ct))
            .ToDictionary(i => i.Id, i => (i.Amount, i.Currency));

        IReadOnlyList<DealDto> dtos = deals
            .Select(d => CreateDealCommandHandler.ToDto(d, _links, MatchedFor(d, investments)))
            .ToList();

        return Result.Success(dtos);
    }

    private static (decimal Amount, string Currency)? MatchedFor(
        Domain.Deals.Deal deal, IReadOnlyDictionary<Guid, (decimal Amount, string Currency)> investments)
        => deal.MatchedInvestmentId is { } id && investments.TryGetValue(id, out var m) ? m : null;
}
