namespace Atria.Application.Investments.Dtos;

/// <summary>
/// What an entered sum buys in an offering: the whole tokens it covers, what those cost, and what is
/// left over. Nothing is reserved by asking.
/// </summary>
/// <param name="PropertyId">The offering quoted.</param>
/// <param name="RequestedAmount">The sum the investor entered.</param>
/// <param name="TokenCount">Whole tokens that sum covers, rounded down.</param>
/// <param name="TotalAmount"><paramref name="TokenCount"/> × <paramref name="PricePerToken"/> — what the investor would actually be charged.</param>
/// <param name="ChangeAmount"><paramref name="RequestedAmount"/> − <paramref name="TotalAmount"/>: the part of the entered sum that buys nothing. Always smaller than one token's price.</param>
/// <param name="PricePerToken">Unit price the quote was computed at.</param>
/// <param name="Currency">Currency of every amount here.</param>
/// <param name="MinPurchaseTokens">Fewest tokens this offering accepts in one application.</param>
/// <param name="MinPurchaseAmount">What that minimum costs — the sum to show when a quote is refused for being too small.</param>
/// <param name="AvailableTokens">Tokens still unplaced in the offering.</param>
/// <param name="CanPurchase">Whether an application for <paramref name="TokenCount"/> would be accepted: at least the minimum, and no more than what is left.</param>
/// <param name="AreaEquivalentSqM">Floor area the holding would stand for, or <c>null</c> when the unit's area is unknown. An equivalent for display — the token is a share of the issue, not a metre.</param>
/// <param name="SharePercent">Share of the whole issue the holding would be, in percent.</param>
public sealed record InvestmentQuoteDto(
    Guid PropertyId,
    decimal RequestedAmount,
    long TokenCount,
    decimal TotalAmount,
    decimal ChangeAmount,
    decimal PricePerToken,
    string Currency,
    long MinPurchaseTokens,
    decimal MinPurchaseAmount,
    long AvailableTokens,
    bool CanPurchase,
    decimal? AreaEquivalentSqM,
    decimal SharePercent);
