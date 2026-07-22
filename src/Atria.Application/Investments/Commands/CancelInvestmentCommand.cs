using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>Cancels the caller's own reserved application, returning its tokens to the pool.</summary>
/// <param name="InvestmentId">The application to cancel.</param>
public sealed record CancelInvestmentCommand(Guid InvestmentId) : IRequest<Result>;

/// <summary>
/// Lets an investor withdraw their own Reserved application before it is approved: moves it to
/// Cancelled and returns the reserved tokens to the property's pool (one unit of work). The caller
/// must own the investment (else 404, no existence leak) and it must still be awaiting approval (409).
/// </summary>
public sealed class CancelInvestmentCommandHandler : IRequestHandler<CancelInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;

    public CancelInvestmentCommandHandler(
        IInvestmentRepository investments,
        IPropertyRepository properties,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser)
    {
        _investments = investments;
        _properties = properties;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(CancelInvestmentCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure(Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        // Resource-based authorization: report someone else's investment as not found.
        if (investment is null || investment.InvestorId != userId.Value)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        if (investment.Status != InvestmentStatus.Reserved)
            return Result.Failure(Error.Conflict(
                "investment.notReserved", "Only a reserved application awaiting approval can be cancelled."));

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.property_unavailable", "Property not found."));

        investment.Cancel();
        property.ReleaseTokens(investment.TokenCount);

        _investments.Update(investment);
        _properties.Update(property);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
