namespace Atria.Application.Investments.Dtos;

/// <summary>Aggregated portfolio view for the current investor.</summary>
/// <param name="TotalInvested">Sum of all invested amounts across the investor's investments.</param>
/// <param name="ActiveCount">Number of currently active investments.</param>
/// <param name="Investments">The underlying investments that make up the portfolio.</param>
public sealed record PortfolioDto(
    decimal TotalInvested,
    int ActiveCount,
    IReadOnlyList<InvestmentDto> Investments);
