using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Trading;

namespace Atria.Application.Trading.Queries;

/// <summary>One thing that does not add up between the operator's reports and the chain.</summary>
/// <param name="Code">Stable identifier of the check that failed.</param>
/// <param name="Detail">What was expected and what was found, in words an operator can act on.</param>
/// <param name="Reference">The trade number or transaction the finding is about, for the incident.</param>
public sealed record TradeDiscrepancy(string Code, string Detail, string? Reference);

/// <summary>The outcome of comparing an issue's trade reports against the chain.</summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="CheckedAtUtc">When the comparison ran.</param>
/// <param name="FromUtc">Start of the period examined.</param>
/// <param name="ToUtc">End of the period examined.</param>
/// <param name="ReportedTrades">Settlements reported by the operator in the period.</param>
/// <param name="MatchedTrades">Of those, how many agree with a transfer on chain.</param>
/// <param name="ChainTransfers">Holder-to-holder transfers read from the chain in the period.</param>
/// <param name="Discrepancies">Everything that disagreed; empty means the two sides reconcile.</param>
public sealed record TradeReconciliationReport(
    Guid PropertyId,
    DateTime CheckedAtUtc,
    DateTime FromUtc,
    DateTime ToUtc,
    int ReportedTrades,
    int MatchedTrades,
    int ChainTransfers,
    IReadOnlyList<TradeDiscrepancy> Discrepancies)
{
    /// <summary>True when nothing disagreed.</summary>
    public bool IsClean => Discrepancies.Count == 0;
}

/// <summary>
/// Compares what the trading operator reported against what the chain shows, for one issue.
/// </summary>
/// <param name="PropertyId">The issue to check.</param>
/// <param name="FromUtc">Start of the period; defaults to the last 24 hours.</param>
/// <param name="ToUtc">End of the period; defaults to now.</param>
public sealed record ReconcileTradesQuery(Guid PropertyId, DateTime? FromUtc, DateTime? ToUtc)
    : IRequest<Result<TradeReconciliationReport>>;

/// <summary>
/// Runs the checks that decide whether the operator's reporting can be relied on.
/// </summary>
/// <remarks>
/// <para>
/// Four questions, and each catches a different failure. A card that never matched a transfer means
/// the operator reported a trade that did not settle the way they said. A holder-to-holder transfer
/// with no card means shares moved on an exchange and we were not told — the case that leaves us
/// valuing a transfer at nominal price and filing an incomplete disclosure. A mismatch means both
/// sides described the same transaction differently. A gap in the sequence means an event was lost
/// in transit and neither side would otherwise notice.
/// </para>
/// <para>
/// Note what this deliberately does not do: it never rewrites the register from the operator's
/// numbers. The chain says who holds what; the operator says what the trade was. When they conflict,
/// the answer is a finding for a person to resolve, not a silent correction in either direction.
/// </para>
/// </remarks>
public sealed class ReconcileTradesQueryHandler
    : IRequestHandler<ReconcileTradesQuery, Result<TradeReconciliationReport>>
{
    /// <summary>Default window: the daily reconciliation we require the operator to support.</summary>
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);

    private const string ZeroAddress = "0x0000000000000000000000000000000000000000";

    /// <summary>
    /// Blocks left out at the tip, matching the registry sync. Transfers shallower than this have not
    /// been applied yet, so counting them here would report every recent trade as unreported.
    /// </summary>
    private const int ConfirmationDepth = 15;

    private readonly IPropertyRepository _properties;
    private readonly IExchangeTradeRepository _trades;
    private readonly ITokenReader _tokens;
    private readonly IDateTimeProvider _clock;

    public ReconcileTradesQueryHandler(
        IPropertyRepository properties,
        IExchangeTradeRepository trades,
        ITokenReader tokens,
        IDateTimeProvider clock)
    {
        _properties = properties;
        _trades = trades;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<Result<TradeReconciliationReport>> Handle(
        ReconcileTradesQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
        {
            return Result.Failure<TradeReconciliationReport>(
                Error.NotFound("trades.propertyNotFound", "Property not found."));
        }

        if (string.IsNullOrWhiteSpace(property.TokenContractAddress)
            || string.IsNullOrWhiteSpace(property.TokenChain))
        {
            return Result.Failure<TradeReconciliationReport>(Error.Conflict(
                "trades.notDeployed", "This issue has no token contract, so there is nothing to reconcile."));
        }

        var now = _clock.UtcNow;
        var to = request.ToUtc ?? now;
        var from = request.FromUtc ?? to - DefaultWindow;

        if (from >= to)
        {
            return Result.Failure<TradeReconciliationReport>(Error.Validation(
                "trades.badPeriod", "The start of the period must come before its end."));
        }

        var all = await _trades.ListByPropertyAsync(property.Id, ct);

        var settlements = all
            .Where(t => t.EventType == ExchangeTradeType.Settled
                        && !t.IsReversed
                        && t.SettledAtUtc >= from
                        && t.SettledAtUtc <= to)
            .ToList();

        var discrepancies = new List<TradeDiscrepancy>();

        // 1. Cards that claim an on-chain settlement we could never confirm.
        foreach (var trade in settlements.Where(t => t.MatchState == TradeMatchState.AwaitingChain))
        {
            discrepancies.Add(new TradeDiscrepancy(
                "trades.unconfirmed",
                $"Сделка {trade.ExchangeTradeId}: биржа сообщила перевод {trade.TransactionHash}, "
                + "в цепочке он не найден.",
                trade.ExchangeTradeId));
        }

        // 2. Cards that describe the same transaction differently than the chain does.
        foreach (var trade in settlements.Where(t => t.MatchState == TradeMatchState.Mismatched))
        {
            discrepancies.Add(new TradeDiscrepancy(
                "trades.mismatch",
                $"Сделка {trade.ExchangeTradeId} расходится с цепочкой: {trade.MatchDetail}",
                trade.ExchangeTradeId));
        }

        // 3. Gaps in the operator's own numbering: an event that was sent and never arrived.
        var sequences = await _trades.ListSequencesAsync(property.Id, ct);
        foreach (var gap in FindGaps(sequences))
        {
            discrepancies.Add(new TradeDiscrepancy(
                "trades.sequenceGap",
                $"Пропущено событие биржи с номером {gap} — оно не дошло или не было отправлено.",
                gap.ToString()));
        }

        // 4. The check the whole integration exists for: shares moved between holders on chain and
        // no trading card explains it. Without a card the transfer is valued at nominal price and its
        // travel-rule disclosure stays incomplete.
        int chainTransferCount;
        try
        {
            chainTransferCount = await CountUnreportedAsync(
                property.TokenChain, property.TokenContractAddress, all, discrepancies, ct);
        }
        catch (Exception ex)
        {
            // An unreachable node is not a clean reconciliation. Reporting "all good" because the
            // chain could not be asked is exactly the failure this check exists to prevent.
            return Result.Failure<TradeReconciliationReport>(Error.ExternalService(
                "trades.chainUnavailable", $"Could not read the token contract: {ex.Message}"));
        }

        return Result.Success(new TradeReconciliationReport(
            property.Id,
            now,
            from,
            to,
            settlements.Count,
            settlements.Count(t => t.MatchState == TradeMatchState.Matched),
            chainTransferCount,
            discrepancies));
    }

    /// <summary>
    /// Walks recent holder-to-holder transfers and flags any that no trade card accounts for.
    /// </summary>
    private async Task<int> CountUnreportedAsync(
        string chain, string contract, IReadOnlyList<ExchangeTrade> trades,
        List<TradeDiscrepancy> discrepancies, CancellationToken ct)
    {
        var head = await _tokens.GetHeadBlockAsync(chain, ct);
        var safeHead = head - ConfirmationDepth;
        if (safeHead <= 0)
            return 0;

        // A block window rather than a timestamp window: block times are what the chain offers, and
        // converting a period to blocks exactly would need a per-block timestamp lookup for no gain.
        var fromBlock = Math.Max(safeHead - BlocksInWindow, 0);

        var transfers = await _tokens.GetTransfersAsync(chain, contract, fromBlock, safeHead, ct);

        // Mints and burns are our own operations, not trades — the operator owes no card for them.
        var holderToHolder = transfers
            .Where(t => !IsZero(t.From) && !IsZero(t.To))
            .ToList();

        var reported = trades
            .Where(t => !string.IsNullOrWhiteSpace(t.TransactionHash))
            .Select(t => t.TransactionHash!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var transfer in holderToHolder.Where(t => !reported.Contains(t.TransactionHash)))
        {
            discrepancies.Add(new TradeDiscrepancy(
                "trades.unreportedTransfer",
                $"В цепочке перевод {transfer.Amount} долей {transfer.From} -> {transfer.To} "
                + $"(блок {transfer.BlockNumber}), торговой карточки по нему нет.",
                transfer.TransactionHash));
        }

        return holderToHolder.Count;
    }

    /// <summary>
    /// Roughly a day of blocks at a two-second block time, with room to spare. The window only has to
    /// cover the reconciliation period; overlapping it is harmless because a transfer that already
    /// has a card produces no finding.
    /// </summary>
    private const long BlocksInWindow = 50_000;

    /// <summary>
    /// Missing numbers between the lowest and highest sequence received. Only the interior is
    /// checked: numbers above the highest have not been sent yet, and one below the lowest belongs to
    /// a period before this issue started trading.
    /// </summary>
    private static IEnumerable<long> FindGaps(IReadOnlyList<long> sequences)
    {
        if (sequences.Count < 2)
            yield break;

        var seen = sequences.ToHashSet();

        for (var n = sequences[0] + 1; n < sequences[^1]; n++)
        {
            if (!seen.Contains(n))
                yield return n;
        }
    }

    private static bool IsZero(string address)
        => string.Equals(address, ZeroAddress, StringComparison.OrdinalIgnoreCase);
}
