using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Trading;

namespace Atria.Application.Trading.Queries;

/// <summary>A trade card as shown to an operator.</summary>
/// <remarks>
/// The counterparty's name is deliberately absent. It is personal data held solely to complete a
/// travel-rule disclosure, and a trade list is not a screen that needs to show it.
/// </remarks>
public sealed record ExchangeTradeDto(
    Guid Id,
    string EventId,
    ExchangeTradeType EventType,
    long SequenceNo,
    string ExchangeTradeId,
    Guid PropertyId,
    string? TransactionHash,
    string SellerAddress,
    string BuyerAddress,
    long TokenCount,
    decimal PricePerToken,
    decimal TotalValue,
    string Currency,
    DateTime TradedAtUtc,
    DateTime SettledAtUtc,
    TradeMatchState MatchState,
    string? MatchDetail,
    bool IsReversed,
    bool HasCounterpartyIdentity);

/// <summary>Trades reported for an issue, newest settlement first.</summary>
/// <param name="PropertyId">The issue.</param>
public sealed record GetExchangeTradesQuery(Guid PropertyId)
    : IRequest<Result<IReadOnlyList<ExchangeTradeDto>>>;

public sealed class GetExchangeTradesQueryHandler
    : IRequestHandler<GetExchangeTradesQuery, Result<IReadOnlyList<ExchangeTradeDto>>>
{
    private readonly IExchangeTradeRepository _trades;

    public GetExchangeTradesQueryHandler(IExchangeTradeRepository trades) => _trades = trades;

    public async Task<Result<IReadOnlyList<ExchangeTradeDto>>> Handle(
        GetExchangeTradesQuery request, CancellationToken ct)
    {
        var trades = await _trades.ListByPropertyAsync(request.PropertyId, ct);

        IReadOnlyList<ExchangeTradeDto> dtos = trades
            .Select(t => new ExchangeTradeDto(
                t.Id, t.EventId, t.EventType, t.SequenceNo, t.ExchangeTradeId, t.PropertyId,
                t.TransactionHash, t.SellerAddress, t.BuyerAddress, t.TokenCount, t.PricePerToken,
                t.TotalValue, t.Currency, t.TradedAtUtc, t.SettledAtUtc, t.MatchState, t.MatchDetail,
                t.IsReversed,
                !string.IsNullOrWhiteSpace(t.BeneficiaryName)
                    || !string.IsNullOrWhiteSpace(t.CounterpartyVasp)))
            .ToList();

        return Result.Success(dtos);
    }
}
