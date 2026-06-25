using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Factories;
using Atria.Domain.Kyc;

namespace Atria.Application.Applications.Commands;

/// <summary>Creates a draft investment application for the current investor.</summary>
public sealed record CreateApplicationCommand(Guid PropertyId, decimal Amount) : IRequest<Result<Guid>>;

/// <summary>
/// Loads the current investor's KYC profile, enforces approved-KYC + positive-amount
/// invariants via the factory, persists the draft application, and returns its id.
/// </summary>
public sealed class CreateApplicationCommandHandler
    : IRequestHandler<CreateApplicationCommand, Result<Guid>>
{
    private readonly IApplicationRepository _applications;
    private readonly IKycRepository _kyc;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CreateApplicationCommandHandler(
        IApplicationRepository applications,
        IKycRepository kyc,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _kyc = kyc;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(CreateApplicationCommand request, CancellationToken ct)
    {
        var investorId = _currentUser.UserId;
        if (investorId is null)
            return Result.Failure<Guid>(Error.Unauthorized("application.unauthorized", "Authentication required."));

        // Approved KYC is a precondition for applying — enforced here and in the factory.
        var kyc = await _kyc.GetByUserIdAsync(investorId.Value, ct);
        if (kyc is null || kyc.Status != KycStatus.Approved)
            return Result.Failure<Guid>(Error.Forbidden("application.kyc_required", "Approved KYC is required to apply."));

        // Property must exist, be active, and have enough remaining token capacity for the
        // requested amount. This is an early UX guard; the authoritative supply decrement
        // happens on activation (AllocateTokensOnInvestmentActivatedHandler).
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null || !property.IsActive)
            return Result.Failure<Guid>(Error.NotFound("application.property_unavailable", "Property not found or inactive."));

        var remainingCapacity = property.AvailableTokens * property.TokenPrice;
        if (request.Amount > remainingCapacity)
            return Result.Failure<Guid>(Error.Conflict(
                "application.insufficient_tokens", "Requested amount exceeds the property's remaining token capacity."));

        var application = InvestorApplicationFactory.Create(
            investorId.Value, request.PropertyId, request.Amount, kyc.Status);

        await _applications.AddAsync(application, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(application.Id);
    }
}
