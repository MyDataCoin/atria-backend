namespace Atria.Application.Audit.Dtos;

/// <summary>Read model for a single audit log row returned to Admin/Compliance.</summary>
/// <param name="EntityType">Name of the aggregate/entity the event concerns (e.g. <c>Investment</c>, <c>KycProfile</c>).</param>
/// <param name="EntityId">Identifier of the affected entity, when the event is tied to a specific instance.</param>
/// <param name="EventType">Name of the recorded domain event (e.g. <c>InvestmentActivated</c>).</param>
/// <param name="DataJson">Serialized JSON snapshot of the event payload, when captured.</param>
/// <param name="UserId">Identifier of the user who triggered the event, when attributable.</param>
/// <param name="OccurredOnUtc">UTC timestamp at which the event occurred.</param>
public sealed record AuditLogDto(
    string EntityType,
    Guid? EntityId,
    string EventType,
    string? DataJson,
    Guid? UserId,
    DateTime OccurredOnUtc);
