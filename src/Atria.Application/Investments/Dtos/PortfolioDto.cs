namespace Atria.Application.Investments.Dtos;

/// <summary>Aggregated portfolio view for the current investor.</summary>
public sealed record PortfolioDto(
    decimal TotalInvested,
    int ActiveCount,
    IReadOnlyList<InvestmentDto> Investments);
