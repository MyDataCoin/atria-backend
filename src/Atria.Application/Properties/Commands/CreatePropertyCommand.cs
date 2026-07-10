using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>Creates a new tokenized property. Admin only.</summary>
public sealed record CreatePropertyCommand(
    string Name,
    string? Description,
    string? Address,
    decimal TotalValue,
    decimal TokenPrice,
    long TotalTokens,
    string Currency,
    string? PropertyType = null,
    string? City = null,
    int? YearBuilt = null,
    string? Developer = null,
    int? Floors = null) : IRequest<Result<Guid>>;
