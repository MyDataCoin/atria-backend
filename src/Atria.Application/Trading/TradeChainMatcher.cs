using Atria.Application.Abstractions;
using Atria.Domain.Trading;
using Atria.Domain.TravelRule;
using Microsoft.Extensions.Logging;

namespace Atria.Application.Trading;

/// <summary>Joins a reported trade card to the transfer we read from the chain.</summary>
public interface ITradeChainMatcher
{
    /// <summary>
    /// Compares one card against the chain and records the verdict on it. Never throws on a
    /// disagreement — a mismatch is a finding to be shown, not an error to be raised.
    /// </summary>
    Task MatchAsync(ExchangeTrade trade, CancellationToken ct);

    /// <summary>
    /// Re-checks every card of an issue still waiting for its chain leg. Returns how many changed
    /// state. Run after a chain sync, when transfers that cards were waiting for have just landed.
    /// </summary>
    Task<int> MatchOutstandingAsync(Guid propertyId, CancellationToken ct);
}

/// <summary>
/// Decides whether a trade card and an on-chain transfer describe the same event, and enriches what
/// depends on the answer.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is the whole point of the integration. We read share movements from the chain; the
/// operator reports price and parties. Neither half is usable alone, and joining them on the
/// transaction hash is only sound if the two halves actually agree — same contract, same seller, same
/// buyer, same number of shares. When they agree, the card's price replaces the nominal valuation
/// everything downstream had to assume. When they disagree, the register keeps what the chain said
/// and the card is flagged: the chain is what happened, the card is what someone reported about it.
/// </para>
/// <para>
/// Addresses are compared case-insensitively. Ethereum addresses are hex with an optional checksum
/// casing, so the same address legitimately arrives spelled two ways, and a case-sensitive comparison
/// would manufacture mismatches out of formatting.
/// </para>
/// </remarks>
public sealed class TradeChainMatcher : ITradeChainMatcher
{
    private readonly IExchangeTradeRepository _trades;
    private readonly ITokenReader _tokens;
    private readonly ITravelRuleRepository _travelRule;
    private readonly IPropertyRepository _properties;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<TradeChainMatcher> _logger;

    public TradeChainMatcher(
        IExchangeTradeRepository trades,
        ITokenReader tokens,
        ITravelRuleRepository travelRule,
        IPropertyRepository properties,
        IDateTimeProvider clock,
        ILogger<TradeChainMatcher> logger)
    {
        _trades = trades;
        _tokens = tokens;
        _travelRule = travelRule;
        _properties = properties;
        _clock = clock;
        _logger = logger;
    }

    public async Task MatchAsync(ExchangeTrade trade, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(trade);

        // Off-chain settlement will never have a transfer to match, and a reversal is not owed one.
        if (trade.MatchState is TradeMatchState.OffChain or TradeMatchState.Reversed)
            return;
        if (string.IsNullOrWhiteSpace(trade.TransactionHash))
            return;

        var property = await _properties.GetByIdAsync(trade.PropertyId, ct);
        if (property?.TokenChain is null || property.TokenContractAddress is null)
            return;

        IReadOnlyList<TokenTransfer> transfers;
        try
        {
            transfers = await _tokens.GetTransfersByTransactionAsync(
                property.TokenChain, property.TokenContractAddress, trade.TransactionHash, ct);
        }
        catch (Exception ex)
        {
            // An unreachable node leaves the card waiting rather than declaring it mismatched. "We
            // could not ask" must never be recorded as "the answer disagreed".
            _logger.LogWarning(
                "Could not read transaction {TxHash} while matching trade {ExchangeTradeId}: {Reason}",
                trade.TransactionHash, trade.ExchangeTradeId, ex.Message);
            return;
        }

        if (transfers.Count == 0)
        {
            // The usual case for a fresh card: the transaction exists but the sync has not reached a
            // confirmation depth where we will apply it. Waiting is correct.
            return;
        }

        var now = _clock.UtcNow;

        var exact = transfers.FirstOrDefault(t =>
            SameAddress(t.From, trade.SellerAddress)
            && SameAddress(t.To, trade.BuyerAddress)
            && t.Amount == trade.TokenCount);

        if (exact is not null)
        {
            trade.MarkMatched(now);
            _trades.Update(trade);

            await EnrichTravelRuleAsync(trade, ct);
            return;
        }

        trade.MarkMismatched(DescribeMismatch(trade, transfers), now);
        _trades.Update(trade);

        // An incident by definition: two records of the same event that do not agree. Whoever reads
        // the log has to be able to open the trade with the operator by its own number.
        _logger.LogError(
            "Trade {ExchangeTradeId} does not match the chain on {TxHash}: {Detail}",
            trade.ExchangeTradeId, trade.TransactionHash, trade.MatchDetail);
    }

    public async Task<int> MatchOutstandingAsync(Guid propertyId, CancellationToken ct)
    {
        var pending = await _trades.ListAwaitingChainAsync(propertyId, ct);
        var changed = 0;

        foreach (var trade in pending)
        {
            var before = trade.MatchState;
            await MatchAsync(trade, ct);

            if (trade.MatchState != before)
                changed++;
        }

        return changed;
    }

    /// <summary>
    /// Fills in what the disclosure could not know on its own: who the counterparty is, and what the
    /// transfer was actually worth.
    /// </summary>
    /// <remarks>
    /// The value matters as much as the name. Until a card arrives, a transfer is valued at the
    /// issue's nominal price, which mis-states it in both directions — a trade above the reporting
    /// threshold can be missed, and one below it reported needlessly. Only the execution price
    /// settles that, and only the operator has it.
    /// </remarks>
    private async Task EnrichTravelRuleAsync(ExchangeTrade trade, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(trade.BeneficiaryName)
            && string.IsNullOrWhiteSpace(trade.CounterpartyVasp))
        {
            return;
        }

        var message = await _travelRule.FindByTransferAsync(
            trade.TransactionHash!, trade.SellerAddress, trade.BuyerAddress, ct);

        if (message is null)
            return;

        // A disclosure already handed over is frozen: what was sent has to stay answerable exactly as
        // it was sent, even when better information arrives afterwards.
        if (message.Status is TravelRuleStatus.Sent or TravelRuleStatus.Acknowledged)
        {
            _logger.LogInformation(
                "Trade {ExchangeTradeId} identified the counterparty, but the disclosure for {TxHash} "
                + "has already been delivered; it is left as sent.",
                trade.ExchangeTradeId, trade.TransactionHash);
            return;
        }

        if (!string.IsNullOrWhiteSpace(trade.BeneficiaryName))
            message.SetBeneficiary(trade.BeneficiaryName, trade.CounterpartyVasp);

        _travelRule.Update(message);

        _logger.LogInformation(
            "Travel-rule disclosure for {TxHash} completed from trade {ExchangeTradeId}.",
            trade.TransactionHash, trade.ExchangeTradeId);
    }

    private static string DescribeMismatch(ExchangeTrade trade, IReadOnlyList<TokenTransfer> transfers)
    {
        // Narrow to the transfer between the same parties, if there is one: then the disagreement is
        // about the amount, which is a far more specific thing to tell the operator than "no match".
        var samePair = transfers.FirstOrDefault(t =>
            SameAddress(t.From, trade.SellerAddress) && SameAddress(t.To, trade.BuyerAddress));

        if (samePair is not null)
        {
            return $"Биржа сообщила {trade.TokenCount} долей, в цепочке по этому переводу "
                   + $"{samePair.Amount}.";
        }

        var observed = string.Join(
            "; ", transfers.Select(t => $"{t.From} -> {t.To}: {t.Amount}"));

        return $"Биржа сообщила перевод {trade.SellerAddress} -> {trade.BuyerAddress} "
               + $"({trade.TokenCount} долей), в транзакции таких переводов нет. Найдено: {observed}.";
    }

    private static bool SameAddress(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
