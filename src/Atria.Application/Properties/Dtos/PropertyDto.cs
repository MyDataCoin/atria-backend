namespace Atria.Application.Properties.Dtos;

/// <summary>Read model of a property and its current token supply.</summary>
public sealed record PropertyDto(
    Guid Id,
    string Name,
    string? Description,
    decimal TokenPrice,
    long AvailableTokens,
    long TotalTokens,
    string Currency,
    bool IsActive);
