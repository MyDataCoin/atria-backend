using Atria.Domain.Common;

namespace Atria.Domain.Trading.Events;

/// <summary>
/// A secondary-market trade in an issue's shares was reported by the trading operator. Carries the
/// execution price, which is what downstream reporting could not previously establish.
/// </summary>
/// <remarks>
/// The operator's own event identifier is <c>ExchangeEventId</c>, not <c>EventId</c>: the latter is
/// the outbox's identifier for this domain event, and conflating the two would make an incident
/// impossible to trace back to the message the operator actually sent.
/// </remarks>
public sealed record ExchangeTradeSettledEvent(
    Guid TradeId,
    string ExchangeEventId,
    string ExchangeTradeId,
    Guid PropertyId,
    string? TransactionHash,
    string SellerAddress,
    string BuyerAddress,
    long TokenCount,
    decimal PricePerToken,
    string Currency) : DomainEventBase;

/// <summary>A previously reported trade was reversed by the trading operator.</summary>
public sealed record ExchangeTradeReversedEvent(
    Guid TradeId,
    string ExchangeEventId,
    string ExchangeTradeId,
    Guid PropertyId) : DomainEventBase;
