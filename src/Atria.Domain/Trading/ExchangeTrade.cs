using Atria.Domain.Common;
using Atria.Domain.Trading.Events;

namespace Atria.Domain.Trading;

/// <summary>What kind of thing the exchange told us about.</summary>
public enum ExchangeTradeType
{
    /// <summary>A trade settled. Carries the price and the parties.</summary>
    Settled = 0,

    /// <summary>A previously reported trade was reversed. Refers back to the original event.</summary>
    Cancelled = 1
}

/// <summary>How the trade card stands against the transfer we read from the chain.</summary>
public enum TradeMatchState
{
    /// <summary>
    /// Reported with a transaction hash, but no chain transfer carrying that hash has been read yet.
    /// The normal state for a card that arrives before the block is deep enough to be applied.
    /// </summary>
    AwaitingChain = 0,

    /// <summary>The card and a chain transfer agree on contract, parties and share count.</summary>
    Matched = 1,

    /// <summary>
    /// A chain transfer with this hash exists but disagrees with the card — different parties, or a
    /// different number of shares. An incident: one of the two records is wrong.
    /// </summary>
    Mismatched = 2,

    /// <summary>
    /// The exchange stated there is no on-chain leg (settlement is internal to them). Nothing will
    /// ever match; the card stands on its own and the shares sit on the address it names.
    /// </summary>
    OffChain = 3,

    /// <summary>The trade was reversed, so no match is owed.</summary>
    Reversed = 4
}

/// <summary>
/// The trading context of one secondary-market trade in our shares, as reported by the licensed
/// trading operator: price, trade number, parties, and the settlement moment.
/// </summary>
/// <remarks>
/// <para>
/// We see the share movement ourselves — a <c>Transfer</c> event is read from the chain and the
/// register updates without anyone telling us. What the chain does not carry is the trade: at what
/// price it executed, under what number, and who the counterparty is as a client. A transfer without
/// those cannot be reported to a regulator, cannot value the issue, and cannot satisfy the travel
/// rule. This aggregate is the half only the exchange can supply, and
/// <see cref="TransactionHash"/> is the seam that joins the two halves.
/// </para>
/// <para>
/// Everything here is reported by an outside party, so the type is built to be defensive.
/// <see cref="EventId"/> is the exchange's own event identifier and is unique: a redelivery lands on
/// the same row rather than a second trade. A card is never edited after the fact — a trade that
/// turns out wrong is reversed with a separate <see cref="ExchangeTradeType.Cancelled"/> event that
/// points back at the original, because silently rewriting a card would change reporting that has
/// already gone out.
/// </para>
/// </remarks>
public sealed class ExchangeTrade : AggregateRoot
{
    /// <summary>The exchange's identifier for this event. The idempotency key: unique per operator.</summary>
    public string EventId { get; private set; } = null!;

    /// <summary>Settlement or reversal.</summary>
    public ExchangeTradeType EventType { get; private set; }

    /// <summary>
    /// Monotonic per-issue counter from the exchange. A gap in the sequence means an event was lost
    /// in transit, which is only visible if they number what they send.
    /// </summary>
    public long SequenceNo { get; private set; }

    /// <summary>The exchange's trade number — what an incident is discussed by.</summary>
    public string ExchangeTradeId { get; private set; } = null!;

    /// <summary>The issue, resolved from the reported contract address.</summary>
    public Guid PropertyId { get; private set; }

    public string Chain { get; private set; } = null!;
    public string TokenContract { get; private set; } = null!;

    /// <summary>
    /// The on-chain settlement transaction, when there is one. Null means the exchange declared the
    /// trade settled off chain (see <see cref="TradeMatchState.OffChain"/>).
    /// </summary>
    public string? TransactionHash { get; private set; }

    /// <summary>Block the settlement transaction was mined in, as reported.</summary>
    public long? BlockNumber { get; private set; }

    public string SellerAddress { get; private set; } = null!;
    public string BuyerAddress { get; private set; } = null!;

    /// <summary>Shares traded. Whole units — the contract has no decimals.</summary>
    public long TokenCount { get; private set; }

    /// <summary>
    /// Execution price of one share. The number the whole integration exists for: without it a
    /// transfer is valued at the issue's nominal price, which is wrong for reporting and wrong for
    /// judging a travel-rule threshold in both directions.
    /// </summary>
    public decimal PricePerToken { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>Value of the trade at the execution price.</summary>
    public decimal TotalValue => PricePerToken * TokenCount;

    public DateTime TradedAtUtc { get; private set; }

    /// <summary>The moment from which the buyer counts as a holder.</summary>
    public DateTime SettledAtUtc { get; private set; }

    /// <summary>
    /// The counterparty's service provider, when the other side is not our client. Feeds the travel
    /// rule disclosure we cannot complete on our own.
    /// </summary>
    public string? CounterpartyVasp { get; private set; }

    /// <summary>The counterparty's name, when the other side is not our client.</summary>
    public string? BeneficiaryName { get; private set; }

    /// <summary>
    /// The address the shares are actually held on when settlement is off chain — typically the
    /// exchange's own omnibus address.
    /// </summary>
    public string? CustodyAddress { get; private set; }

    public TradeMatchState MatchState { get; private set; }

    /// <summary>What disagreed, when the card and the chain do not match.</summary>
    public string? MatchDetail { get; private set; }

    /// <summary>When the card was last compared against the chain.</summary>
    public DateTime? MatchedAtUtc { get; private set; }

    /// <summary>
    /// For a reversal, the <see cref="EventId"/> of the trade being reversed. For a settlement, null.
    /// </summary>
    public string? ReversesEventId { get; private set; }

    /// <summary>Set on a settlement once a reversal for it has been received.</summary>
    public bool IsReversed { get; private set; }

    /// <summary>When the exchange said the event happened, as received.</summary>
    public DateTime ReceivedAtUtc { get; private set; }

    private ExchangeTrade() { }

    /// <summary>
    /// Records a settled trade as reported. Validates only what makes a card usable at all; whether it
    /// agrees with the chain is a separate question answered by <see cref="MarkMatched"/> and
    /// <see cref="MarkMismatched"/>.
    /// </summary>
    public static ExchangeTrade Settle(
        string eventId, long sequenceNo, string exchangeTradeId, Guid propertyId, string chain,
        string tokenContract, string? transactionHash, long? blockNumber, string sellerAddress,
        string buyerAddress, long tokenCount, decimal pricePerToken, string currency,
        DateTime tradedAtUtc, DateTime settledAtUtc, string? counterpartyVasp, string? beneficiaryName,
        string? custodyAddress, DateTime receivedAtUtc)
    {
        Require(eventId, "An exchange event id is required.");
        Require(exchangeTradeId, "An exchange trade id is required.");
        Require(chain, "A chain is required.");
        Require(tokenContract, "A token contract is required.");
        Require(sellerAddress, "A seller address is required.");
        Require(buyerAddress, "A buyer address is required.");
        Require(currency, "A currency is required.");

        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (sequenceNo <= 0)
            throw new DomainException("A positive sequence number is required.");
        if (tokenCount <= 0)
            throw new DomainException("A trade must move a positive whole number of shares.");
        if (pricePerToken <= 0)
            throw new DomainException("An execution price is required; a trade at zero cannot be reported.");
        if (string.Equals(sellerAddress, buyerAddress, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("A trade cannot have the same address on both sides.");

        // An off-chain settlement has to say where the shares actually sit, otherwise the register has
        // no way to account for them and the exchange's omnibus address looks like an ordinary holder.
        var isOnChain = !string.IsNullOrWhiteSpace(transactionHash);
        if (!isOnChain && string.IsNullOrWhiteSpace(custodyAddress))
        {
            throw new DomainException(
                "A trade settled off chain must name the address the shares are held on.");
        }

        var trade = new ExchangeTrade
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = ExchangeTradeType.Settled,
            SequenceNo = sequenceNo,
            ExchangeTradeId = exchangeTradeId,
            PropertyId = propertyId,
            Chain = chain,
            TokenContract = tokenContract,
            TransactionHash = transactionHash,
            BlockNumber = blockNumber,
            SellerAddress = sellerAddress,
            BuyerAddress = buyerAddress,
            TokenCount = tokenCount,
            PricePerToken = pricePerToken,
            Currency = currency,
            TradedAtUtc = tradedAtUtc,
            SettledAtUtc = settledAtUtc,
            CounterpartyVasp = counterpartyVasp,
            BeneficiaryName = beneficiaryName,
            CustodyAddress = custodyAddress,
            ReceivedAtUtc = receivedAtUtc,
            MatchState = isOnChain ? TradeMatchState.AwaitingChain : TradeMatchState.OffChain
        };

        trade.RaiseEvent(new ExchangeTradeSettledEvent(
            trade.Id, trade.EventId, trade.ExchangeTradeId, trade.PropertyId, trade.TransactionHash,
            trade.SellerAddress, trade.BuyerAddress, trade.TokenCount, trade.PricePerToken,
            trade.Currency));

        return trade;
    }

    /// <summary>
    /// Records a reversal of an earlier trade. Kept as its own row referring back to the original,
    /// rather than deleting or editing it: what was reported once has to stay answerable, including
    /// the fact that it was taken back.
    /// </summary>
    public static ExchangeTrade Reverse(
        string eventId, long sequenceNo, string reversesEventId, ExchangeTrade original,
        DateTime receivedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(original);
        Require(eventId, "An exchange event id is required.");
        Require(reversesEventId, "A reversal must name the event it reverses.");

        if (sequenceNo <= 0)
            throw new DomainException("A positive sequence number is required.");
        if (original.EventType != ExchangeTradeType.Settled)
            throw new DomainException("Only a settled trade can be reversed.");

        return new ExchangeTrade
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            EventType = ExchangeTradeType.Cancelled,
            SequenceNo = sequenceNo,
            ReversesEventId = reversesEventId,
            ExchangeTradeId = original.ExchangeTradeId,
            PropertyId = original.PropertyId,
            Chain = original.Chain,
            TokenContract = original.TokenContract,
            TransactionHash = original.TransactionHash,
            BlockNumber = original.BlockNumber,
            SellerAddress = original.SellerAddress,
            BuyerAddress = original.BuyerAddress,
            TokenCount = original.TokenCount,
            PricePerToken = original.PricePerToken,
            Currency = original.Currency,
            TradedAtUtc = original.TradedAtUtc,
            SettledAtUtc = original.SettledAtUtc,
            ReceivedAtUtc = receivedAtUtc,
            MatchState = TradeMatchState.Reversed
        };
    }

    /// <summary>Marks this settlement as taken back by a later reversal event.</summary>
    public void MarkReversedBy(string reversalEventId)
    {
        Require(reversalEventId, "A reversal event id is required.");

        if (EventType != ExchangeTradeType.Settled)
            throw new DomainException("Only a settled trade can be reversed.");

        IsReversed = true;
        MatchState = TradeMatchState.Reversed;
        MatchDetail = $"Сторнирована событием {reversalEventId}.";

        RaiseEvent(new ExchangeTradeReversedEvent(Id, EventId, ExchangeTradeId, PropertyId));
    }

    /// <summary>Records that the card agrees with the transfer read from the chain.</summary>
    public void MarkMatched(DateTime nowUtc)
    {
        if (MatchState == TradeMatchState.Reversed)
            return;

        MatchState = TradeMatchState.Matched;
        MatchDetail = null;
        MatchedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records that a transfer with this hash exists but says something different. Not an exception:
    /// the card is kept exactly as reported so the disagreement can be shown to the exchange.
    /// </summary>
    public void MarkMismatched(string detail, DateTime nowUtc)
    {
        Require(detail, "A mismatch needs a description.");

        if (MatchState == TradeMatchState.Reversed)
            return;

        MatchState = TradeMatchState.Mismatched;
        MatchDetail = detail;
        MatchedAtUtc = nowUtc;
    }

    private static void Require(string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(message);
    }
}
