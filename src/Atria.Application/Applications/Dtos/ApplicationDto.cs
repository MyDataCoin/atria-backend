using Atria.Domain.Applications;

namespace Atria.Application.Applications.Dtos;

/// <summary>Read model for an investor application.</summary>
/// <param name="Id">The application's unique identifier.</param>
/// <param name="PropertyId">Identifier of the property the investor is applying to invest in.</param>
/// <param name="Amount">The amount the investor committed in the application.</param>
/// <param name="Status">Current lifecycle status (Draft, Submitted, UnderReview, Approved, Rejected).</param>
/// <param name="RejectionReason">Reason supplied by Compliance when the application was rejected; null otherwise.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the application was created.</param>
public sealed record ApplicationDto(
    Guid Id,
    Guid PropertyId,
    decimal Amount,
    ApplicationStatus Status,
    string? RejectionReason,
    DateTime CreatedAtUtc);
