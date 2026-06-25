using Atria.Domain.Investments;

namespace Atria.Application.Investments.Dtos;

/// <summary>Read model of a single investment.</summary>
public sealed record InvestmentDto(
    Guid Id,
    Guid PropertyId,
    decimal Amount,
    string Currency,
    InvestmentStatus Status,
    DateTime CreatedAtUtc);
