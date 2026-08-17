using Atria.Domain.Payouts;

namespace Atria.Application.Payouts.Dtos;

/// <summary>A distribution as the operator's list shows it — no holder lines.</summary>
/// <param name="Id">The distribution.</param>
/// <param name="PropertyId">The issue being distributed on.</param>
/// <param name="SnapshotId">The frozen register it was computed against.</param>
/// <param name="SnapshotAtUtc">The cut of that register.</param>
/// <param name="Kind">Income distribution or return of capital.</param>
/// <param name="Method">Bank transfer or wallet.</param>
/// <param name="Status">Draft, approved, completed or cancelled.</param>
/// <param name="DeclaredAmount">The total declared.</param>
/// <param name="PaidAmount">What has actually gone out so far.</param>
/// <param name="OutstandingAmount">What is still owed, failed lines included.</param>
/// <param name="Currency">Currency of every amount here.</param>
/// <param name="TotalTokens">Shares the amount was divided across.</param>
/// <param name="HolderCount">How many holders the distribution covers.</param>
/// <param name="PaidCount">How many of them have been paid.</param>
/// <param name="ApprovedAtUtc">When the second approval opened it for settlement.</param>
/// <param name="CompletedAtUtc">When it was closed.</param>
/// <param name="Note">What the distribution is, in the operator's words.</param>
/// <param name="CancellationReason">Why it was called off, if it was.</param>
/// <param name="CreatedAtUtc">When it was drawn up.</param>
public sealed record PayoutRunDto(
    Guid Id,
    Guid PropertyId,
    Guid SnapshotId,
    DateTime SnapshotAtUtc,
    PayoutKind Kind,
    PayoutMethod Method,
    PayoutRunStatus Status,
    decimal DeclaredAmount,
    decimal PaidAmount,
    decimal OutstandingAmount,
    string Currency,
    decimal TotalTokens,
    int HolderCount,
    int PaidCount,
    DateTime? ApprovedAtUtc,
    DateTime? CompletedAtUtc,
    string? Note,
    string? CancellationReason,
    DateTime CreatedAtUtc);

/// <summary>One holder's line of a distribution.</summary>
/// <param name="Id">The line.</param>
/// <param name="InvestorId">The investor behind the address at the cut.</param>
/// <param name="WalletAddress">The holding address at the cut.</param>
/// <param name="TokenCount">Shares held at the cut — the basis of the amount.</param>
/// <param name="Amount">What this holder is owed.</param>
/// <param name="Status">Pending, paid, or failed delivery.</param>
/// <param name="SettlementReference">Payment order number or transaction hash.</param>
/// <param name="PaidAtUtc">When the payment was recorded.</param>
/// <param name="FailureReason">Why delivery failed, if it did.</param>
public sealed record PayoutItemDto(
    Guid Id,
    Guid InvestorId,
    string WalletAddress,
    decimal TokenCount,
    decimal Amount,
    PayoutItemStatus Status,
    string? SettlementReference,
    DateTime? PaidAtUtc,
    string? FailureReason);

/// <summary>A distribution with every holder line — the settlement working view.</summary>
/// <param name="Run">The distribution header.</param>
/// <param name="Items">Its holder lines, largest amount first.</param>
public sealed record PayoutRunDetailDto(PayoutRunDto Run, IReadOnlyList<PayoutItemDto> Items);

/// <summary>
/// One distribution as it appears to the holder it concerns: what they were owed, on what holding,
/// and whether it has been paid.
/// </summary>
/// <param name="RunId">The distribution.</param>
/// <param name="PropertyId">The issue it was paid on.</param>
/// <param name="Kind">Income distribution or return of capital.</param>
/// <param name="Method">How it was sent.</param>
/// <param name="SnapshotAtUtc">The cut their holding was measured at.</param>
/// <param name="TokenCount">Shares they held at that cut.</param>
/// <param name="Amount">Their share of the distribution.</param>
/// <param name="Currency">Currency of the amount.</param>
/// <param name="Status">Whether it has reached them.</param>
/// <param name="SettlementReference">The reference the payment came back with.</param>
/// <param name="PaidAtUtc">When it was paid.</param>
public sealed record MyPayoutDto(
    Guid RunId,
    Guid PropertyId,
    PayoutKind Kind,
    PayoutMethod Method,
    DateTime SnapshotAtUtc,
    decimal TokenCount,
    decimal Amount,
    string Currency,
    PayoutItemStatus Status,
    string? SettlementReference,
    DateTime? PaidAtUtc);

/// <summary>Maps payout aggregates to their read models.</summary>
public static class PayoutMapping
{
    /// <summary>
    /// Header view. The paid/outstanding totals are computed from the lines rather than stored, so
    /// they cannot drift away from what the lines actually say.
    /// </summary>
    public static PayoutRunDto ToDto(this PayoutRun run)
        => new(
            run.Id, run.PropertyId, run.SnapshotId, run.SnapshotAtUtc, run.Kind, run.Method,
            run.Status, run.DeclaredAmount, run.PaidAmount, run.OutstandingAmount, run.Currency,
            run.TotalTokens, run.Items.Count,
            run.Items.Count(i => i.Status == PayoutItemStatus.Paid),
            run.ApprovedAtUtc, run.CompletedAtUtc, run.Note, run.CancellationReason, run.CreatedAtUtc);

    public static PayoutItemDto ToDto(this PayoutItem item)
        => new(
            item.Id, item.InvestorId, item.WalletAddress, item.TokenCount, item.Amount, item.Status,
            item.SettlementReference, item.PaidAtUtc, item.FailureReason);
}
