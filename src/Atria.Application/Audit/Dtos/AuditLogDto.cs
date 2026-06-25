namespace Atria.Application.Audit.Dtos;

/// <summary>Read model for a single audit log row returned to Admin/Compliance.</summary>
public sealed record AuditLogDto(
    string EntityType,
    Guid? EntityId,
    string EventType,
    string? DataJson,
    Guid? UserId,
    DateTime OccurredOnUtc);
