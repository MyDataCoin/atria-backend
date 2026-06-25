using Atria.Domain.Applications;

namespace Atria.Application.Applications.Dtos;

/// <summary>Read model for an investor application.</summary>
public sealed record ApplicationDto(
    Guid Id,
    Guid PropertyId,
    decimal Amount,
    ApplicationStatus Status,
    string? RejectionReason,
    DateTime CreatedAtUtc);
