using Atria.Domain.Investments;

namespace Atria.Application.Properties.Dtos;

/// <summary>Read model of a property and its current token supply.</summary>
/// <param name="Id">The property's unique identifier.</param>
/// <param name="Name">Display name of the property.</param>
/// <param name="Description">Optional longer description of the property.</param>
/// <param name="TokenPrice">Price of a single token, in the property's currency.</param>
/// <param name="AvailableTokens">Number of tokens still available for investment.</param>
/// <param name="TotalTokens">Total number of tokens the property was issued with.</param>
/// <param name="Currency">3-letter ISO currency code of the token price (e.g. USD, KGS).</param>
/// <param name="Status">Lifecycle status, lowercase: <c>draft</c> | <c>coming_soon</c> | <c>open</c> | <c>completed</c>.</param>
/// <param name="Images">The property's photos (max 3), each with a public URL.</param>
/// <param name="Documents">The property's documents, each with a public URL.</param>
public sealed record PropertyDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TokenPrice,
    long AvailableTokens,
    long TotalTokens,
    string Currency,
    string Status,
    IReadOnlyList<PropertyImageDto> Images,
    IReadOnlyList<PropertyDocumentDto> Documents)
{
    /// <summary>Maps the domain status to its lowercase wire value.</summary>
    public static string ToWireStatus(PropertyStatus status) => status switch
    {
        PropertyStatus.ComingSoon => "coming_soon",
        PropertyStatus.Open => "open",
        PropertyStatus.Completed => "completed",
        _ => "draft"
    };
}
