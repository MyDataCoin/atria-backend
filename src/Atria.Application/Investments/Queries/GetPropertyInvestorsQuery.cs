using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;

namespace Atria.Application.Investments.Queries;

/// <summary>Lists the Active investors of a property with their token holdings. Admin/Compliance.</summary>
public sealed record GetPropertyInvestorsQuery(Guid PropertyId)
    : IRequest<Result<IReadOnlyList<PropertyInvestorDto>>>;

/// <summary>
/// Aggregates a property's Active investments per investor: tokens = round(Σ amount / token price).
/// FullName comes from the KYC profile, decrypted by the EF value converter when it is materialized
/// in the repository (same service as /users). Share percent is intentionally NOT returned — the
/// client computes it as tokens / totalTokens * 100.
/// </summary>
public sealed class GetPropertyInvestorsQueryHandler
    : IRequestHandler<GetPropertyInvestorsQuery, Result<IReadOnlyList<PropertyInvestorDto>>>
{
    private readonly IInvestmentRepository _investments;

    public GetPropertyInvestorsQueryHandler(IInvestmentRepository investments) => _investments = investments;

    public async Task<Result<IReadOnlyList<PropertyInvestorDto>>> Handle(
        GetPropertyInvestorsQuery request, CancellationToken ct)
    {
        var rows = await _investments.GetActiveByPropertyAsync(request.PropertyId, ct);

        IReadOnlyList<PropertyInvestorDto> dtos = rows
            .GroupBy(r => r.InvestorId)
            .Select(g =>
            {
                var tokens = g.Sum(r => r.TokenPrice > 0 ? r.Amount / r.TokenPrice : 0m);
                return new PropertyInvestorDto(
                    g.First().Kyc?.FullName,
                    (int)Math.Round(tokens, MidpointRounding.AwayFromZero));
            })
            .ToList();

        return Result.Success(dtos);
    }
}
