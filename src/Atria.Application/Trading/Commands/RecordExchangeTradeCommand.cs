using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Common;
using Atria.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Trading.Commands;

/// <summary>One trade event exactly as the trading operator reported it.</summary>
/// <remarks>
/// Deliberately a flat, primitive-typed record rather than the domain aggregate: this is untrusted
/// input that has been parsed but not yet believed. Every field is optional at this layer so that a
/// malformed event produces a validation failure with a reason, not a deserialization crash.
/// </remarks>
/// <param name="EventId">The operator's event identifier. Idempotency key.</param>
/// <param name="EventType"><c>trade_settled</c> or <c>trade_cancelled</c>.</param>
/// <param name="SequenceNo">Monotonic per-issue counter, so a lost event is visible as a gap.</param>
/// <param name="ExchangeTradeId">The operator's trade number.</param>
/// <param name="Chain">Chain the settlement happened on.</param>
/// <param name="TokenContract">Contract address; how the issue is resolved.</param>
/// <param name="TransactionHash">Settlement transaction, or null when settlement is off chain.</param>
/// <param name="BlockNumber">Block the settlement was mined in, when on chain.</param>
/// <param name="SellerAddress">Address the shares left.</param>
/// <param name="BuyerAddress">Address the shares arrived at.</param>
/// <param name="TokenCount">Whole shares traded.</param>
/// <param name="PricePerToken">Execution price of one share.</param>
/// <param name="Currency">Currency the price is quoted in.</param>
/// <param name="TradedAtUtc">When the trade executed.</param>
/// <param name="SettledAtUtc">When the buyer became the holder.</param>
/// <param name="CounterpartyVasp">Counterparty's service provider, for the travel rule.</param>
/// <param name="BeneficiaryName">Counterparty's name, for the travel rule.</param>
/// <param name="CustodyAddress">Where shares are held when settlement is off chain.</param>
/// <param name="ReversesEventId">For a reversal, the event being reversed.</param>
public sealed record ExchangeTradeEventDto(
    string? EventId,
    string? EventType,
    long SequenceNo,
    string? ExchangeTradeId,
    string? Chain,
    string? TokenContract,
    string? TransactionHash,
    long? BlockNumber,
    string? SellerAddress,
    string? BuyerAddress,
    long TokenCount,
    decimal PricePerToken,
    string? Currency,
    DateTime TradedAtUtc,
    DateTime SettledAtUtc,
    string? CounterpartyVasp,
    string? BeneficiaryName,
    string? CustodyAddress,
    string? ReversesEventId);

/// <summary>What became of a reported trade event.</summary>
/// <param name="TradeId">Our identifier for the recorded card.</param>
/// <param name="Accepted">False when the event was a duplicate that changed nothing.</param>
/// <param name="MatchState">How the card stands against the chain right now.</param>
public sealed record ExchangeTradeReceipt(Guid TradeId, bool Accepted, TradeMatchState MatchState);

/// <summary>
/// Records one trade event from the licensed trading operator.
/// </summary>
/// <param name="Event">The parsed event.</param>
public sealed record RecordExchangeTradeCommand(ExchangeTradeEventDto Event)
    : IRequest<Result<ExchangeTradeReceipt>>;

/// <summary>
/// Accepts a trade card from the trading operator and joins it to what we already read from the
/// chain.
/// </summary>
/// <remarks>
/// <para>
/// The transport has already verified the signature, so the body is authentic — but authentic is not
/// the same as correct. This handler still resolves the contract to an issue itself rather than
/// trusting a property id, refuses an event whose numbers do not hold together, and records a
/// disagreement with the chain instead of overwriting our own reading with theirs. The chain remains
/// the source of truth for who holds what; the operator is the source of truth for price and parties.
/// </para>
/// <para>
/// Idempotency is by the operator's <c>event_id</c>: a redelivery returns the receipt of the row that
/// already exists and touches nothing. That is what allows them to retry safely for a day, which is
/// what we require of them.
/// </para>
/// </remarks>
public sealed class RecordExchangeTradeCommandHandler
    : IRequestHandler<RecordExchangeTradeCommand, Result<ExchangeTradeReceipt>>
{
    private const string SettledType = "trade_settled";
    private const string CancelledType = "trade_cancelled";

    private readonly IExchangeTradeRepository _trades;
    private readonly IPropertyRepository _properties;
    private readonly ITradeChainMatcher _matcher;
    private readonly IDateTimeProvider _clock;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<RecordExchangeTradeCommandHandler> _logger;

    public RecordExchangeTradeCommandHandler(
        IExchangeTradeRepository trades,
        IPropertyRepository properties,
        ITradeChainMatcher matcher,
        IDateTimeProvider clock,
        IUnitOfWork uow,
        ILogger<RecordExchangeTradeCommandHandler> logger)
    {
        _trades = trades;
        _properties = properties;
        _matcher = matcher;
        _clock = clock;
        _uow = uow;
        _logger = logger;
    }

    public async Task<Result<ExchangeTradeReceipt>> Handle(
        RecordExchangeTradeCommand request, CancellationToken ct)
    {
        var dto = request.Event;

        if (string.IsNullOrWhiteSpace(dto.EventId))
        {
            return Result.Failure<ExchangeTradeReceipt>(Error.Validation(
                "trade.eventIdRequired", "event_id is required; it is the idempotency key."));
        }

        // Idempotency before anything else. A redelivery is the expected case, not an error: we ask
        // the operator to retry until we answer 2xx, so the second delivery must be cheap and silent.
        var existing = await _trades.FindByEventIdAsync(dto.EventId, ct);
        if (existing is not null)
        {
            return Result.Success(new ExchangeTradeReceipt(
                existing.Id, Accepted: false, existing.MatchState));
        }

        var eventType = (dto.EventType ?? string.Empty).Trim().ToLowerInvariant();
        return eventType switch
        {
            SettledType => await SettleAsync(dto, ct),
            CancelledType => await ReverseAsync(dto, ct),
            _ => Result.Failure<ExchangeTradeReceipt>(Error.Validation(
                "trade.unknownEventType",
                $"Unknown event_type '{dto.EventType}'. Expected '{SettledType}' or '{CancelledType}'."))
        };
    }

    private async Task<Result<ExchangeTradeReceipt>> SettleAsync(
        ExchangeTradeEventDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.TokenContract) || string.IsNullOrWhiteSpace(dto.Chain))
        {
            return Result.Failure<ExchangeTradeReceipt>(Error.Validation(
                "trade.contractRequired", "chain and token_contract are required to identify the issue."));
        }

        // The issue is resolved from the contract we ourselves deployed, never from an identifier
        // supplied in the body. An event naming a contract we do not run is not ours to record.
        var property = await _properties.FindByTokenContractAsync(dto.Chain, dto.TokenContract, ct);
        if (property is null)
        {
            return Result.Failure<ExchangeTradeReceipt>(Error.NotFound(
                "trade.unknownContract",
                $"No issue is registered for contract {dto.TokenContract} on {dto.Chain}."));
        }

        var now = _clock.UtcNow;

        ExchangeTrade trade;
        try
        {
            trade = ExchangeTrade.Settle(
                dto.EventId!, dto.SequenceNo, dto.ExchangeTradeId!, property.Id, dto.Chain,
                dto.TokenContract, Normalize(dto.TransactionHash), dto.BlockNumber,
                dto.SellerAddress!, dto.BuyerAddress!, dto.TokenCount, dto.PricePerToken,
                dto.Currency!, dto.TradedAtUtc, dto.SettledAtUtc, dto.CounterpartyVasp,
                dto.BeneficiaryName, Normalize(dto.CustodyAddress), now);
        }
        catch (DomainException ex)
        {
            // 400, not 500: the operator sent something that cannot be a trade, and repeating it will
            // not help. Their retry policy should not spin on a permanently malformed event.
            return Result.Failure<ExchangeTradeReceipt>(Error.Validation("trade.invalid", ex.Message));
        }

        await _trades.AddAsync(trade, ct);

        // Match immediately: the transfer is usually already in the register, because a card arrives
        // after settlement and the sync runs continuously. When it is not, the card waits and the
        // sweep picks it up.
        await _matcher.MatchAsync(trade, ct);

        await _uow.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Exchange trade {ExchangeTradeId} recorded for {Property}: {Tokens} share(s) at "
            + "{Price} {Currency}, match {MatchState}.",
            trade.ExchangeTradeId, property.Id, trade.TokenCount, trade.PricePerToken,
            trade.Currency, trade.MatchState);

        return Result.Success(new ExchangeTradeReceipt(trade.Id, Accepted: true, trade.MatchState));
    }

    private async Task<Result<ExchangeTradeReceipt>> ReverseAsync(
        ExchangeTradeEventDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ReversesEventId))
        {
            return Result.Failure<ExchangeTradeReceipt>(Error.Validation(
                "trade.reversalTargetRequired",
                "A trade_cancelled event must name the event_id it reverses."));
        }

        var original = await _trades.FindSettlementByEventIdAsync(dto.ReversesEventId, ct);
        if (original is null)
        {
            // Not a 404 the operator should give up on: the settlement may still be in flight behind
            // this reversal. A retryable failure lets their queue redeliver once ordering settles.
            return Result.Failure<ExchangeTradeReceipt>(Error.Conflict(
                "trade.reversalTargetUnknown",
                $"No settled trade was received under event_id {dto.ReversesEventId}."));
        }

        var now = _clock.UtcNow;

        ExchangeTrade reversal;
        try
        {
            reversal = ExchangeTrade.Reverse(dto.EventId!, dto.SequenceNo, dto.ReversesEventId, original, now);
            original.MarkReversedBy(dto.EventId!);
        }
        catch (DomainException ex)
        {
            return Result.Failure<ExchangeTradeReceipt>(Error.Validation("trade.invalid", ex.Message));
        }

        await _trades.AddAsync(reversal, ct);
        _trades.Update(original);
        await _uow.SaveChangesAsync(ct);

        // Loud on purpose. A reversal after we have already valued the transfer, and possibly already
        // sent a travel-rule disclosure on it, is something a person has to look at.
        _logger.LogWarning(
            "Exchange trade {ExchangeTradeId} ({EventId}) was reversed by {ReversalEventId}. Any "
            + "reporting already based on it must be revisited.",
            original.ExchangeTradeId, original.EventId, reversal.EventId);

        return Result.Success(new ExchangeTradeReceipt(
            reversal.Id, Accepted: true, TradeMatchState.Reversed));
    }

    /// <summary>Blank strings from a JSON body mean "absent", not "empty value".</summary>
    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
