using Atria.Domain.Common;

namespace Atria.Domain.Audit;

/// <summary>
/// Append-only audit log row. NOT an aggregate root (it raises no events and is
/// itself the record of something that already happened). Immutable after
/// creation: every property has a private setter and there are no mutators.
/// Created either from a domain event (<see cref="FromDomainEvent"/>) by the
/// universal audit handler, or for PII/resource access logging
/// (<see cref="ForAccess"/>).
/// </summary>
public sealed class AuditLogEntry : Entity
{
    public string EntityType { get; private set; } = default!;
    public Guid? EntityId { get; private set; }
    public string EventType { get; private set; } = default!;
    public string? DataJson { get; private set; }
    public Guid? UserId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }

    // Parameterless ctor for EF rehydration only.
    private AuditLogEntry() { }

    private AuditLogEntry(
        string entityType,
        Guid? entityId,
        string eventType,
        string? dataJson,
        Guid? userId,
        string? correlationId,
        DateTime occurredOnUtc)
    {
        Id = Guid.NewGuid();
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        DataJson = dataJson;
        UserId = userId;
        CorrelationId = correlationId;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>
    /// Build an audit entry from a domain event. The event's own occurrence
    /// timestamp is preserved; <paramref name="dataJson"/> is the serialized
    /// event payload (sanitized of secrets/PII upstream).
    /// </summary>
    public static AuditLogEntry FromDomainEvent(
        IDomainEvent e,
        string entityType,
        Guid? entityId,
        string? dataJson,
        string? correlationId)
        => new(
            entityType,
            entityId,
            e.GetType().Name,
            dataJson,
            userId: null,
            correlationId,
            e.OccurredOnUtc);

    /// <summary>
    /// Build an audit entry for a resource/PII access (e.g. reading an encrypted
    /// KYC document). Stamped with the current UTC time.
    /// </summary>
    public static AuditLogEntry ForAccess(
        string entityType,
        Guid? entityId,
        string action,
        Guid? userId,
        string? correlationId)
        => new(
            entityType,
            entityId,
            action,
            dataJson: null,
            userId,
            correlationId,
            DateTime.UtcNow);
}
