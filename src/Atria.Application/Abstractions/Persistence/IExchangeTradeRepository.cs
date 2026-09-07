using Atria.Domain.Trading;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="ExchangeTrade"/> — the trading context reported by the
/// licensed trading operator for secondary-market trades in our shares.
/// </summary>
public interface IExchangeTradeRepository : IRepository<ExchangeTrade>
{
    /// <summary>
    /// The trade already recorded under this operator event id, or null. The idempotency lookup: a
    /// redelivery must land on the existing row instead of creating a second trade.
    /// </summary>
    Task<ExchangeTrade?> FindByEventIdAsync(string eventId, CancellationToken ct);

    /// <summary>
    /// The settled trade this reversal refers back to, or null when we never received it. Tracked, so
    /// the caller can mark it reversed.
    /// </summary>
    Task<ExchangeTrade?> FindSettlementByEventIdAsync(string eventId, CancellationToken ct);

    /// <summary>
    /// Trades reported for a transaction hash. More than one is legitimate — a single transaction can
    /// carry several transfers — so the caller picks the one whose parties match.
    /// </summary>
    Task<IReadOnlyList<ExchangeTrade>> ListByTransactionAsync(
        string transactionHash, CancellationToken ct);

    /// <summary>Trades reported for an issue, newest settlement first.</summary>
    Task<IReadOnlyList<ExchangeTrade>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// Trades for an issue that were reported with an on-chain leg but have not been confirmed
    /// against the chain yet. What the matcher works through.
    /// </summary>
    Task<IReadOnlyList<ExchangeTrade>> ListAwaitingChainAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// The highest sequence number accepted for an issue, or 0 when nothing has been received. Used
    /// to notice that the operator's numbering has skipped ahead.
    /// </summary>
    Task<long> GetHighestSequenceAsync(Guid propertyId, CancellationToken ct);

    /// <summary>
    /// Sequence numbers accepted for an issue within a range, so gaps can be listed exactly rather
    /// than inferred from the maximum alone.
    /// </summary>
    Task<IReadOnlyList<long>> ListSequencesAsync(Guid propertyId, CancellationToken ct);
}
