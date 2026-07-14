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

    /// <summary>
    /// Denormalized display name of whoever triggered the action, captured at write time (a raw
    /// user id is useless in the journal, and an investor is not in the admin registry). Null for
    /// system-generated entries.
    /// </summary>
    public string? ActorName { get; private set; }

    /// <summary>
    /// Ready-to-render, human-readable description of what happened. Composed server-side so the
    /// client never has to re-derive domain wording from the raw payload.
    /// </summary>
    public string? Summary { get; private set; }

    /// <summary>How critical the action is; drives the journal's severity filter.</summary>
    public AuditSeverity Severity { get; private set; }

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
        string? actorName,
        string? summary,
        AuditSeverity severity,
        string? correlationId,
        DateTime occurredOnUtc)
    {
        Id = Guid.NewGuid();
        EntityType = entityType;
        EntityId = entityId;
        EventType = eventType;
        DataJson = dataJson;
        UserId = userId;
        ActorName = actorName;
        Summary = summary;
        Severity = severity;
        CorrelationId = correlationId;
        OccurredOnUtc = occurredOnUtc;
    }

    /// <summary>
    /// Build an audit entry for an explicit, attributable action recorded inside the command that
    /// performed it — so the actor, the human-readable summary and the severity are all known, and
    /// the row commits in the SAME transaction as the action itself.
    /// </summary>
    public static AuditLogEntry ForAction(
        string entityType,
        Guid? entityId,
        string eventType,
        Guid? userId,
        string? actorName,
        string? summary,
        AuditSeverity severity,
        DateTime occurredOnUtc,
        string? dataJson = null,
        string? correlationId = null)
        => new(
            entityType,
            entityId,
            eventType,
            dataJson,
            userId,
            actorName,
            summary,
            severity,
            correlationId,
            occurredOnUtc);

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
            // Background (outbox) path: no HTTP context, so no actor and no composed summary.
            actorName: null,
            summary: null,
            AuditSeverity.Success,
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
            actorName: null,
            summary: null,
            AuditSeverity.Success,
            correlationId,
            DateTime.UtcNow);
}
