using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Factories;
using Atria.Domain.Kyc;

namespace Atria.Application.Investments.Commands;

/// <summary>Creates a PendingPayment investment for the current investor.</summary>
public sealed record CreateInvestmentCommand(Guid PropertyId, decimal Amount) : IRequest<Result<Guid>>;

/// <summary>
/// Enforces approved-KYC and property availability, then creates the investment
/// (PendingPayment) directly for the current investor and returns its id. Payment is
/// started separately via <see cref="CreatePaymentSessionCommand"/>.
/// </summary>
public sealed class CreateInvestmentCommandHandler
    : IRequestHandler<CreateInvestmentCommand, Result<Guid>>
{
    private readonly IInvestmentRepository _investments;
    private readonly IKycRepository _kyc;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CreateInvestmentCommandHandler(
        IInvestmentRepository investments,
        IKycRepository kyc,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _investments = investments;
        _kyc = kyc;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
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

        // Property must exist, be active, and have enough remaining token capacity for the
        // requested amount. This is an early UX guard; the authoritative supply decrement
        // happens on activation (AllocateTokensOnInvestmentActivatedHandler).
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null || !property.IsActive)
            return Result.Failure<Guid>(Error.NotFound("investment.property_unavailable", "Property not found or inactive."));

        var remainingCapacity = property.AvailableTokens * property.TokenPrice;
        if (request.Amount > remainingCapacity)
            return Result.Failure<Guid>(Error.Conflict(
                "investment.insufficient_tokens", "Requested amount exceeds the property's remaining token capacity."));

        // Whole tokens the amount buys at the property's unit price.
        var tokenCount = (long)Math.Floor(request.Amount / property.TokenPrice);
        if (tokenCount <= 0)
            return Result.Failure<Guid>(Error.Conflict(
                "investment.amount_too_low", "The amount must cover at least one token."));

        // The property defines the settlement currency for the investment.
        var investment = InvestmentFactory.CreateForInvestor(
            investorId.Value, request.PropertyId, tokenCount, request.Amount, property.Currency);

        await _investments.AddAsync(investment, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(investment.Id);
    }
}
