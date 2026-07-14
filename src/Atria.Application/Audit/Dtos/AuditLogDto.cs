using Atria.Domain.Audit;

namespace Atria.Application.Audit.Dtos;

/// <summary>Read model for a single audit journal row returned to Admin/Compliance.</summary>
/// <param name="Id">Unique identifier of the entry.</param>
/// <param name="EntityType">Aggregate/entity the action concerns (e.g. <c>Property</c>, <c>Publication</c>, <c>SupportTicket</c>).</param>
/// <param name="EntityId">Identifier of the affected instance, when the action is tied to one.</param>
/// <param name="EventType">Action name (e.g. <c>PropertyPublished</c>, <c>TicketOpened</c>).</param>
/// <param name="Summary">Ready-to-render Russian description of what happened; composed server-side.</param>
/// <param name="Severity">Criticality, lowercase: <c>success</c> | <c>warning</c> | <c>alert</c>.</param>
/// <param name="UserId">Who triggered it; <c>null</c> for system-generated entries. Not always an admin — an investor opening a ticket is audited too.</param>
/// <param name="ActorName">Denormalized display name of the actor; <c>null</c> for system-generated entries.</param>
/// <param name="DataJson">Raw serialized event payload, when captured. Diagnostic detail, not display copy.</param>
/// <param name="OccurredOnUtc">UTC timestamp at which the action occurred.</param>
public sealed record AuditLogDto(
    Guid Id,
    string EntityType,
    Guid? EntityId,
    string EventType,
    string? Summary,
    string Severity,
    Guid? UserId,
    string? ActorName,
    string? DataJson,
    DateTime OccurredOnUtc)
{
    /// <summary>Maps a domain entry to the wire shape.</summary>
    public static AuditLogDto From(AuditLogEntry e)
        => new(
            e.Id,
            e.EntityType,
            e.EntityId,
            e.EventType,
            e.Summary,
            ToWireSeverity(e.Severity),
            e.UserId,
            e.ActorName,
            e.DataJson,
            e.OccurredOnUtc);

    /// <summary>Maps the domain severity to its lowercase wire value.</summary>
    public static string ToWireSeverity(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Warning => "warning",
        AuditSeverity.Alert => "alert",
        _ => "success"
    };

    /// <summary>Parses a wire severity value; returns false for an unknown one.</summary>
    public static bool TryParseSeverity(string? raw, out AuditSeverity severity)
    {
        switch (raw?.Trim().ToLowerInvariant())
        {
            case "success": severity = AuditSeverity.Success; return true;
            case "warning": severity = AuditSeverity.Warning; return true;
            case "alert": severity = AuditSeverity.Alert; return true;
            default: severity = default; return false;
        }
    }
}
