using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Deals.Dtos;

namespace Atria.Application.Deals.Queries;

/// <summary>Resolves a referral token to the property it targets, for the investor landing page.</summary>
public sealed record ResolveReferralQuery(string ReferralToken) : IRequest<Result<ReferralResolutionDto>>;

/// <summary>
/// Public read of a referral token. Returns the target property and whether the link is still live,
/// but never the realtor's commission. Reports an unknown token as not found.
/// </summary>
public sealed class ResolveReferralQueryHandler
    : IRequestHandler<ResolveReferralQuery, Result<ReferralResolutionDto>>
{
    private readonly IDealRepository _deals;
    private readonly IDateTimeProvider _clock;

    public ResolveReferralQueryHandler(IDealRepository deals, IDateTimeProvider clock)
    {
        _deals = deals;
        _clock = clock;
    }

    public async Task<Result<ReferralResolutionDto>> Handle(ResolveReferralQuery request, CancellationToken ct)
    {
        var deal = await _deals.GetByReferralTokenAsync(request.ReferralToken, ct);
        if (deal is null)
            return Result.Failure<ReferralResolutionDto>(
                Error.NotFound("referral.not_found", "Referral link not found."));

        return Result.Success(new ReferralResolutionDto(
            deal.PropertyId, deal.IsRedeemable(_clock.UtcNow), deal.ExpiresAtUtc));
    }
}
