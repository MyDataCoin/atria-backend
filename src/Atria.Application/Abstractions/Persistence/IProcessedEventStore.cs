namespace Atria.Application.Abstractions;

/// <summary>
/// Idempotency ledger for exactly-once effects. A handler that moves money or
/// tokens records a stable key (event id + handler name, or external op id)
/// before acting; a retry sees the key and becomes a no-op.
/// </summary>
public interface IProcessedEventStore
{
    Task<bool> IsProcessedAsync(string key, CancellationToken ct);
    Task MarkProcessedAsync(string key, CancellationToken ct);
}
