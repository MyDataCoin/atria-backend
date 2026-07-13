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
    private readonly ICurrentUserService _currentUser;
    private readonly IReferralLinkBuilder _links;

    public GetMyDealsQueryHandler(
        IDealRepository deals, ICurrentUserService currentUser, IReferralLinkBuilder links)
    {
        _deals = deals;
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

        IReadOnlyList<DealDto> dtos = deals
            .Select(d => CreateDealCommandHandler.ToDto(d, _links))
            .ToList();

        return Result.Success(dtos);
    }
}
