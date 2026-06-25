namespace Atria.Infrastructure.Persistence;

/// <summary>
/// Idempotency ledger row (infra-only EF entity, not a domain type). The presence
/// of a key means an exactly-once effect has already been applied.
/// </summary>
public sealed class ProcessedEvent
{
    // Stable idempotency key, e.g. "{HandlerName}:{EventId}". Primary key.
    public string Key { get; set; } = default!;
    public DateTime ProcessedAtUtc { get; set; }
}
