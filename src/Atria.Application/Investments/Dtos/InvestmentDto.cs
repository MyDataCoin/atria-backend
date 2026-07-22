using Atria.Domain.Investments;

namespace Atria.Application.Investments.Dtos;

/// <summary>Read model of a single investment.</summary>
/// <param name="Id">Unique identifier of the investment.</param>
/// <param name="PropertyId">Identifier of the property the investment is in.</param>
/// <param name="TokenCount">How many tokens the investor bought.</param>
/// <param name="Amount">Invested amount in <paramref name="Currency"/>.</param>
/// <param name="Currency">ISO currency code of the amount (for example <c>USD</c>).</param>
/// <param name="PricePerToken">Unit token price snapshot at the time the application was made.</param>
/// <param name="Status">Current lifecycle status of the investment (serialized by name).</param>
/// <param name="OnChainStatus">On-chain confirmation status of the token allocation (serialized by name).</param>
/// <param name="TransactionHash">Mint transaction hash once submitted on chain; <c>null</c> until then.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the investment was created.</param>
public sealed record InvestmentDto(
    Guid Id,
    Guid PropertyId,
    long TokenCount,
    decimal Amount,
    string Currency,
    decimal PricePerToken,
    InvestmentStatus Status,
    OnChainStatus OnChainStatus,
    string? TransactionHash,
    DateTime CreatedAtUtc);
