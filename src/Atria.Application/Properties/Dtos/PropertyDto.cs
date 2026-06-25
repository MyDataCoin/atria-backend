namespace Atria.Application.Properties.Dtos;

/// <summary>Read model of a property and its current token supply.</summary>
/// <param name="Id">The property's unique identifier.</param>
/// <param name="Name">Display name of the property.</param>
/// <param name="Description">Optional longer description of the property.</param>
/// <param name="TokenPrice">Price of a single token, in the property's currency.</param>
/// <param name="AvailableTokens">Number of tokens still available for investment.</param>
/// <param name="TotalTokens">Total number of tokens the property was issued with.</param>
/// <param name="Currency">3-letter ISO currency code of the token price (e.g. USD, KGS).</param>
/// <param name="IsActive">Whether the property is active and open for new applications.</param>
public sealed record PropertyDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TokenPrice,
    long AvailableTokens,
    long TotalTokens,
    string Currency,
    bool IsActive);
