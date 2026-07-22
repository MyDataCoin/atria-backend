using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>Rejects a reserved offering application, returning its tokens to the pool. Operator only.</summary>
/// <param name="InvestmentId">The application to reject.</param>
/// <param name="Reason">Human-readable reason shown to the investor and journalled.</param>
public sealed record RejectInvestmentCommand(Guid InvestmentId, string Reason) : IRequest<Result>;

/// <summary>
/// Moves a Reserved application to Rejected and returns its reserved tokens to the property's pool
/// (both in one unit of work). Raises <c>InvestmentRejectedEvent</c> for the journal/notification.
/// 404 when the application does not exist; 409 when it is no longer awaiting approval.
/// </summary>
public sealed class RejectInvestmentCommandHandler : IRequestHandler<RejectInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _unitOfWork;

    public RejectInvestmentCommandHandler(
        IInvestmentRepository investments, IPropertyRepository properties, IUnitOfWork unitOfWork)
    {
        _investments = investments;
        _properties = properties;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RejectInvestmentCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("investment.reasonRequired", "A rejection reason is required."));

        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        if (investment.Status != InvestmentStatus.Reserved)
            return Result.Failure(Error.Conflict(
                "investment.notReserved", "Only a reserved application awaiting approval can be rejected."));

        var property = await _properties.GetByIdAsync(investment.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("investment.property_unavailable", "Property not found."));

        investment.Reject(request.Reason);
        property.ReleaseTokens(investment.TokenCount);

        _investments.Update(investment);
        _properties.Update(property);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
