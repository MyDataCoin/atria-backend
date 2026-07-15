using Atria.Domain.Investments;

namespace Atria.Application.Investments.Dtos;

/// <summary>
/// One active holding of an investor in a property, for the admin/compliance investor card.
/// One row per property.
/// </summary>
/// <param name="PropertyId">Identifier of the property held.</param>
/// <param name="PropertyName">Denormalized property name (joined), so the client need not fetch the catalog.</param>
/// <param name="TokenCount">How many tokens the investor holds in this property.</param>
/// <param name="Amount">Invested amount in <paramref name="Currency"/> (as stored, not converted).</param>
/// <param name="Currency">ISO currency code of the amount and of the property (for example <c>USD</c>).</param>
/// <param name="SharePercent">
/// The investor's share of the property: <c>TokenCount / property.TotalTokens * 100</c>, computed server-side.
/// </param>
/// <param name="Status">Lifecycle status of the holding (serialized by name); always <c>Active</c> here.</param>
public sealed record UserInvestmentDto(
    Guid PropertyId,
    string PropertyName,
    long TokenCount,
    decimal Amount,
    string Currency,
    decimal SharePercent,
    InvestmentStatus Status);
