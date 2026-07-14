using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Audit;
using Atria.Domain.Common;

namespace Atria.Application.Audit.EventHandlers;

/// <summary>
/// Background audit handler: writes an <c>AuditLogEntry</c> for domain events that are NOT audited
/// explicitly inside their command. The open generic is closed per event type by the dispatcher.
///
/// Two events are skipped: those marked <see cref="IExplicitlyAudited"/> (already journalled with a
/// real actor, so logging them here would duplicate the row anonymously), and those
/// <see cref="AuditNarrator"/> has no Russian description for (KYC and internal plumbing) — the admin
/// journal is an operations log, not a dump of every event. Everything written here is attributed to
/// the system: this runs on the outbox worker, where there is no HTTP context and hence no actor.
/// </summary>
public sealed class AuditAllDomainEventsHandler<TEvent> : IDomainEventHandler<TEvent>
    where TEvent : IDomainEvent
{
    // Common id-bearing property names on Atria events, in priority order.
    private static readonly string[] IdPropertyNames =
    {
        "KycProfileId", "InvestmentId", "InvestorId",
        "PropertyId", "OwnerUserId", "UserId", "Id"
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly IAuditLogRepository _repository;

    public AuditAllDomainEventsHandler(IAuditLogRepository repository)
        => _repository = repository;

    public async Task HandleAsync(TEvent domainEvent, CancellationToken ct)
    {
        // Events already audited inside their command (with the real actor, a summary and a
        // severity) must not be logged a second time here as an anonymous duplicate.
        if (domainEvent is IExplicitlyAudited)
            return;

        // Only events the narrator can describe belong in the admin journal — otherwise the row
        // would show a raw C# class name and an empty details column.
        if (AuditNarrator.Describe(domainEvent) is not { } narration)
            return;

        var (summary, severity) = narration;
        var entityType = AuditNarrator.EntityType(domainEvent);
        var entityId = TryReadEntityId(domainEvent);
        var dataJson = TrySerialize(domainEvent);

        var entry = AuditLogEntry.FromDomainEvent(
            domainEvent,
            entityType,
            entityId,
            AuditNarrator.ActionName(domainEvent),
            summary,
            severity,
            dataJson,
            correlationId: null);

        await _repository.AddAsync(entry, ct);
    }

    /// <summary>
    /// Best-effort: read the first Guid-valued property matching a known id name.
    /// Returns null when none is present.
    /// </summary>
    private static Guid? TryReadEntityId(TEvent domainEvent)
    {
        var type = domainEvent.GetType();
        var prop = AuditEntityIdCache.ResolveIdProperty(type, IdPropertyNames);

        if (prop is not null && prop.GetValue(domainEvent) is Guid value)
        {
            return value;
        }

        return null;
    }

    /// <summary>Serialize the event payload; never let serialization break auditing.</summary>
    private static string? TrySerialize(TEvent domainEvent)
    {
        try
        {
            return JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>
/// Process-wide cache of resolved entity-id properties, keyed by event runtime type.
/// Non-generic so the cache is shared across every closed
/// <see cref="AuditAllDomainEventsHandler{TEvent}"/> instantiation (a static field on
/// the generic handler would be duplicated per closed type).
/// </summary>
internal static class AuditEntityIdCache
{
    // Value may be null: a cached "no matching id property" result for the type.
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> Cache = new();

    /// <summary>
    /// Resolve (and memoize) the first Guid-valued property of <paramref name="eventType"/>
    /// matching one of <paramref name="idPropertyNames"/> in priority order, excluding the
    /// technical <see cref="IDomainEvent.EventId"/>. Returns null when none matches.
    /// </summary>
    public static PropertyInfo? ResolveIdProperty(Type eventType, string[] idPropertyNames)
        => Cache.GetOrAdd(eventType, static (type, names) => FindIdProperty(type, names), idPropertyNames);

    private static PropertyInfo? FindIdProperty(Type eventType, string[] idPropertyNames)
    {
        foreach (var candidate in idPropertyNames)
        {
            var prop = eventType.GetProperty(candidate);
            if (prop is null || prop.PropertyType != typeof(Guid))
            {
                continue;
            }

            // Never use the technical EventId as the entity id.
            if (string.Equals(prop.Name, nameof(IDomainEvent.EventId), StringComparison.Ordinal))
            {
                continue;
            }

            return prop;
        }

        return null;
    }
}
