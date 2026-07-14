using Atria.Domain.Audit;

namespace Atria.Application.Abstractions;

/// <summary>
/// Records an audited action from inside the command that performed it. The entry is ADDED to the
/// current unit of work but not committed here, so it lands in the SAME transaction as the action —
/// an object can never be published while its audit row goes missing.
///
/// The actor (id + display name) is resolved from the current request, so an investor-triggered
/// action (e.g. opening a support ticket) is attributed correctly, not just admin actions.
/// </summary>
public interface IAuditWriter
{
    /// <summary>
    /// Queues one append-only audit entry for the current actor. Call before the handler's
    /// <c>SaveChangesAsync</c>.
    /// </summary>
    /// <param name="entityType">Aggregate kind, e.g. <c>Property</c>, <c>Publication</c>, <c>SupportTicket</c>.</param>
    /// <param name="entityId">The affected instance.</param>
    /// <param name="eventType">Stable action name, e.g. <c>PropertyPublished</c>.</param>
    /// <param name="summary">Ready-to-render Russian description of what happened.</param>
    /// <param name="severity">How critical the action is.</param>
    /// <param name="ct">Cancellation token.</param>
    Task WriteAsync(
        string entityType,
        Guid? entityId,
        string eventType,
        string summary,
        AuditSeverity severity,
        CancellationToken ct);
}
