using Atria.Domain.Investments;

namespace Atria.Application.Investments.Dtos;

/// <summary>Read model of a single investment.</summary>
/// <param name="Id">Unique identifier of the investment.</param>
/// <param name="PropertyId">Identifier of the property the investment is in.</param>
/// <param name="InvestorId">The investor who applied. Carried so the operator queue can name them.</param>
/// <param name="TokenCount">How many tokens the investor bought.</param>
/// <param name="Amount">Invested amount in <paramref name="Currency"/>.</param>
/// <param name="Currency">Currency of the amount. Always <c>KGS</c>.</param>
/// <param name="PricePerToken">Unit token price snapshot at the time the application was made.</param>
/// <param name="Status">Current lifecycle status of the investment (serialized by name).</param>
/// <param name="ReservedUntilUtc">
/// When the reservation lapses if it is not approved before then. Drives the countdown the investor
/// sees on a Reserved application; already in the past for every other status.
/// </param>
/// <param name="WithdrawalDeadlineUtc">
/// Until when the holder may exercise the 14-day right of withdrawal (§44). Null until the
/// application is activated.
/// </param>
/// <param name="RejectionReason">Why an operator declined the application; <c>null</c> unless Rejected.</param>
/// <param name="OnChainStatus">On-chain confirmation status of the token allocation (serialized by name).</param>
/// <param name="TransactionHash">Mint transaction hash once submitted on chain; <c>null</c> until then.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the investment was created.</param>
/// <param name="ReceivedKgs">
/// What actually arrived for this application, in som, when it was paid by crypto invoice. It can be
/// less than <paramref name="Amount"/> when a short payment was accepted and the difference written
/// off. Null when there is no paid invoice (a bank transfer, or not paid yet).
/// </param>
public sealed record InvestmentDto(
    Guid Id,
    Guid PropertyId,
    Guid InvestorId,
    long TokenCount,
    decimal Amount,
    string Currency,
    decimal PricePerToken,
    InvestmentStatus Status,
    DateTime ReservedUntilUtc,
    DateTime? WithdrawalDeadlineUtc,
    string? RejectionReason,
    OnChainStatus OnChainStatus,
    string? TransactionHash,
    DateTime CreatedAtUtc,
    decimal? ReceivedKgs = null)
{
    public static InvestmentDto From(Investment i, IReadOnlyDictionary<Guid, decimal>? received = null) => new(
        i.Id, i.PropertyId, i.InvestorId, i.TokenCount, i.Amount, i.Currency, i.PricePerToken, i.Status,
        i.ReservedUntilUtc, i.WithdrawalDeadlineUtc, i.RejectionReason, i.OnChainStatus, i.TransactionHash,
        i.CreatedAtUtc,
        received is not null && received.TryGetValue(i.Id, out var kgs) ? kgs : null);
}
