using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Commands;

/// <summary>Approves a reserved offering application, activating the investment. Operator only.</summary>
/// <param name="InvestmentId">The application to approve.</param>
public sealed record ApproveInvestmentCommand(Guid InvestmentId) : IRequest<Result>;

/// <summary>
/// Moves a Reserved application to Active. This replaces the old payment callback: an operator
/// confirms the (off-platform) settlement, which raises <c>InvestmentActivatedEvent</c> — allowlisting
/// the wallet, enqueuing the on-chain token allocation, and settling any referral deal. The tokens
/// were already reserved at application time, so the supply is not touched here. 404 when the
/// application does not exist; 409 when it is no longer awaiting approval.
/// </summary>
public sealed class ApproveInvestmentCommandHandler : IRequestHandler<ApproveInvestmentCommand, Result>
{
    private readonly IInvestmentRepository _investments;
    private readonly IUnitOfWork _unitOfWork;

    public ApproveInvestmentCommandHandler(IInvestmentRepository investments, IUnitOfWork unitOfWork)
    {
        _investments = investments;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ApproveInvestmentCommand request, CancellationToken ct)
    {
        var investment = await _investments.GetByIdAsync(request.InvestmentId, ct);
        if (investment is null)
            return Result.Failure(Error.NotFound("investment.notFound", "Investment not found."));

        if (investment.Status != InvestmentStatus.Reserved)
            return Result.Failure(Error.Conflict(
                "investment.notReserved", "Only a reserved application awaiting approval can be approved."));

        investment.Approve();
        _investments.Update(investment);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
