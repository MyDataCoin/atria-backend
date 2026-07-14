using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Deals.Dtos;
using Atria.Domain.Deals;
using Atria.Domain.Investments;

namespace Atria.Application.Deals.Commands;

/// <summary>Creates a Pending referral deal for the current realtor and returns it with its link.</summary>
public sealed record CreateDealCommand(Guid PropertyId, decimal CommissionPercent)
    : IRequest<Result<DealDto>>;

/// <summary>
/// Validates the target property (must exist and be Open for investment), then creates a Pending
/// deal owned by the current realtor with a freshly generated referral link that lives for
/// <see cref="Deal.LinkLifetime"/>.
/// </summary>
public sealed class CreateDealCommandHandler : IRequestHandler<CreateDealCommand, Result<DealDto>>
{
    private readonly IDealRepository _deals;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IReferralLinkBuilder _links;

    public CreateDealCommandHandler(
        IDealRepository deals,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDateTimeProvider clock,
        IReferralLinkBuilder links)
    {
        _deals = deals;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _links = links;
    }

    public async Task<Result<DealDto>> Handle(CreateDealCommand request, CancellationToken ct)
    {
        var realtorId = _currentUser.UserId;
        if (realtorId is null)
            return Result.Failure<DealDto>(Error.Unauthorized("deal.unauthorized", "Authentication required."));

        // The link can only be shared for a property investors can actually buy into.
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null || property.Status != PropertyStatus.Open)
            return Result.Failure<DealDto>(
                Error.NotFound("deal.property_unavailable", "Property not found or not open for investment."));

        var deal = Deal.Create(realtorId.Value, request.PropertyId, request.CommissionPercent, _clock.UtcNow);

        await _deals.AddAsync(deal, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToDto(deal, _links));
    }

    /// <summary>
    /// Maps a deal to its wire shape. Pass <paramref name="matched"/> (the amount + currency of the
    /// deal's matched investment) for a successful deal so the realtor's earnings are computed;
    /// leave it null for pending/rejected deals (no investment yet).
    /// </summary>
    internal static DealDto ToDto(
        Deal deal, IReferralLinkBuilder links, (decimal Amount, string Currency)? matched = null)
    {
        // Earnings = investment amount × commission percent (only when a matched investment exists).
        decimal? commissionAmount = matched is { } m
            ? decimal.Round(m.Amount * deal.CommissionPercent / 100m, 2)
            : null;

        return new DealDto(
            deal.Id,
            deal.PropertyId,
            deal.CommissionPercent,
            deal.ReferralToken,
            links.BuildReferralUrl(deal.ReferralToken),
            DealDto.ToWireStatus(deal.Status),
            deal.ExpiresAtUtc,
            deal.MatchedInvestmentId,
            matched?.Amount,
            matched?.Currency,
            commissionAmount);
    }
}
