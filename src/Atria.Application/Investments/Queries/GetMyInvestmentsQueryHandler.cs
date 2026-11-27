using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Returns the current investor's own investments only.</summary>
public sealed class GetMyInvestmentsQueryHandler
    : IRequestHandler<GetMyInvestmentsQuery, Result<IReadOnlyList<InvestmentDto>>>
{
    private readonly IInvestmentRepository _investments;
    private readonly ICryptoInvoiceRepository _invoices;
    private readonly ICurrentUserService _currentUser;

    public GetMyInvestmentsQueryHandler(IInvestmentRepository investments, ICryptoInvoiceRepository invoices, ICurrentUserService currentUser)
    {
        _investments = investments;
        _invoices = invoices;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<InvestmentDto>>> Handle(GetMyInvestmentsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (userId is null)
            return Result.Failure<IReadOnlyList<InvestmentDto>>(
                Error.Unauthorized("investment.unauthorized", "Authentication is required."));

        var investments = await _investments.GetByInvestorAsync(userId.Value, ct);

        var received = await _invoices.GetReceivedKgsAsync(investments.Select(i => i.Id).ToList(), ct);

        IReadOnlyList<InvestmentDto> dtos = investments
            .Select(i => InvestmentDto.From(i, received))
            .ToList();

        return Result.Success(dtos);
    }
}
