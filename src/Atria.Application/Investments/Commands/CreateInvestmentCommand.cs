using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Factories;
using Atria.Domain.Investments;
using Atria.Domain.Kyc;

namespace Atria.Application.Investments.Commands;

/// <summary>Submits an offering application (Reserved) for the current investor.</summary>
/// <param name="PropertyId">The property to invest in.</param>
/// <param name="Amount">The amount to commit.</param>
/// <param name="ReferralToken">Optional realtor referral token the investor arrived with.</param>
public sealed record CreateInvestmentCommand(Guid PropertyId, decimal Amount, string? ReferralToken = null)
    : IRequest<Result<Guid>>;

/// <summary>
/// Enforces approved-KYC and property availability, reserves the requested tokens from the pool, and
/// creates the application (Reserved) for the current investor, returning its id. Reserving at
/// creation is the authoritative capacity claim, so concurrent applications cannot oversubscribe the
/// last tokens. An operator later approves the application to activate it (there is no payment).
/// </summary>
public sealed class CreateInvestmentCommandHandler
    : IRequestHandler<CreateInvestmentCommand, Result<Guid>>
{
    /// <summary>
    /// How long a reservation is held while it awaits operator approval before its tokens may be
    /// returned to the pool. Generous because approval is a manual back-office action, not a payment
    /// window. (The background release of lapsed reservations is a follow-up.)
    /// </summary>
    private static readonly TimeSpan ReservationWindow = TimeSpan.FromDays(3);

    private readonly IInvestmentRepository _investments;
    private readonly IKycRepository _kyc;
    private readonly IPropertyRepository _properties;
    private readonly IDealRepository _deals;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public CreateInvestmentCommandHandler(
        IInvestmentRepository investments,
        IKycRepository kyc,
        IPropertyRepository properties,
        IDealRepository deals,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _investments = investments;
        _kyc = kyc;
        _properties = properties;
        _deals = deals;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result<Guid>> Handle(CreateInvestmentCommand request, CancellationToken ct)
    {
        var investorId = _currentUser.UserId;
        if (investorId is null)
            return Result.Failure<Guid>(Error.Unauthorized("investment.unauthorized", "Authentication required."));

        // Approved KYC is a precondition for investing. It is re-checked again at payment time.
        var kyc = await _kyc.GetByUserIdAsync(investorId.Value, ct);
        if (kyc is null || kyc.Status != KycStatus.Approved)
            return Result.Failure<Guid>(Error.Forbidden("investment.kyc_required", "Approved KYC is required to invest."));

        // Property must exist, be open for investment, and have enough remaining token capacity
        // for the requested amount. The authoritative supply decrement is the reservation below.
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null || property.Status != PropertyStatus.Open)
            return Result.Failure<Guid>(Error.NotFound("investment.property_unavailable", "Property not found or not open for investment."));

        // Sales can be frozen independently of the lifecycle status.
        if (property.SalesPaused)
            return Result.Failure<Guid>(Error.Conflict("investment.sales_paused", "Purchases are currently paused for this property."));

        var remainingCapacity = property.AvailableTokens * property.TokenPrice;
        if (request.Amount > remainingCapacity)
            return Result.Failure<Guid>(Error.Conflict(
                "investment.insufficient_tokens", "Requested amount exceeds the property's remaining token capacity."));

        // Whole tokens the amount buys at the property's unit price.
        var tokenCount = (long)Math.Floor(request.Amount / property.TokenPrice);
        if (tokenCount <= 0)
            return Result.Failure<Guid>(Error.Conflict(
                "investment.amount_too_low", "The amount must cover at least one token."));

        // If the investor arrived via a realtor's referral link, keep the token only when it still
        // resolves to a redeemable deal for THIS property. A missing/expired/mismatched token is
        // ignored (the purchase proceeds without a referral) rather than blocking the investment.
        string? referralToken = null;
        if (!string.IsNullOrWhiteSpace(request.ReferralToken))
        {
            var deal = await _deals.GetByReferralTokenAsync(request.ReferralToken, ct);
            if (deal is not null && deal.PropertyId == request.PropertyId && deal.IsRedeemable(_clock.UtcNow))
                referralToken = deal.ReferralToken;
        }

        // Reserve the tokens now (authoritative capacity claim). ReserveTokens throws if it would
        // oversubscribe; combined with the property's optimistic-concurrency token, two concurrent
        // applications racing on the last tokens cannot both succeed.
        property.ReserveTokens(tokenCount);
        _properties.Update(property);

        // The property defines the settlement currency and the price snapshot for the application.
        var reservedUntilUtc = _clock.UtcNow.Add(ReservationWindow);
        var investment = InvestmentFactory.CreateForInvestor(
            investorId.Value, request.PropertyId, tokenCount, request.Amount, property.Currency,
            property.TokenPrice, reservedUntilUtc, referralToken);

        await _investments.AddAsync(investment, ct);

        // Property reservation + new application persist in a single unit of work.
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(investment.Id);
    }
}
