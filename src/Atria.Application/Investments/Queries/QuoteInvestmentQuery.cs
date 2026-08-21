using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Investments.Dtos;
using Atria.Domain.Investments;

namespace Atria.Application.Investments.Queries;

/// <summary>
/// What a sum actually buys in an offering, before anything is reserved. The investor types money;
/// the offering is placed in whole tokens; the two rarely land on the same number.
/// </summary>
/// <remarks>
/// This exists so the difference is shown rather than discovered. Without it the investor confirms
/// one sum and the application records another — which is not a rounding detail to them, it is being
/// charged a number they never agreed to.
/// </remarks>
/// <param name="PropertyId">The offering.</param>
/// <param name="Amount">The sum the investor entered.</param>
public sealed record QuoteInvestmentQuery(Guid PropertyId, decimal Amount)
    : IRequest<Result<InvestmentQuoteDto>>;

public sealed class QuoteInvestmentQueryHandler
    : IRequestHandler<QuoteInvestmentQuery, Result<InvestmentQuoteDto>>
{
    private readonly IPropertyRepository _properties;

    public QuoteInvestmentQueryHandler(IPropertyRepository properties) => _properties = properties;

    public async Task<Result<InvestmentQuoteDto>> Handle(QuoteInvestmentQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<InvestmentQuoteDto>(
                Error.NotFound("investment.property_unavailable", "Property not found."));

        // Deliberately the same call the command makes, so the quote cannot promise a count the
        // purchase then declines to give.
        var tokenCount = TokenAmount.FromMoney(request.Amount, property.TokenPrice);

        var acceptable =
            tokenCount >= property.MinPurchaseTokens && tokenCount <= property.AvailableTokens;

        var total = TokenAmount.CostOf(tokenCount, property.TokenPrice);

        return Result.Success(new InvestmentQuoteDto(
            property.Id,
            request.Amount,
            tokenCount,
            total,
            request.Amount - total,
            property.TokenPrice,
            property.Currency,
            property.MinPurchaseTokens,
            property.MinPurchaseAmount,
            property.AvailableTokens,
            acceptable,
            // Whole tokens the area is spread over, so the investor can see what the holding stands
            // for without mistaking the token for a square metre.
            property.AreaPerTokenSqM is { } perToken ? perToken * tokenCount : null,
            property.TotalTokens > 0 ? (decimal)tokenCount / property.TotalTokens * 100m : 0m));
    }
}
