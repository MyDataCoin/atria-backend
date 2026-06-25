using Atria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Atria.Infrastructure.Persistence.Stores;

/// <summary>
/// EF-backed idempotency ledger. A key recorded here marks an exactly-once effect
/// as already applied. Marking adds a row; the surrounding UnitOfWork commits it.
/// </summary>
public sealed class ProcessedEventStore : IProcessedEventStore
{
    private readonly AtriaDbContext _db;

    public ProcessedEventStore(AtriaDbContext db) => _db = db;

    public Task<bool> IsProcessedAsync(string key, CancellationToken ct)
        => _db.ProcessedEvents.AsNoTracking().AnyAsync(p => p.Key == key, ct);

    public async Task MarkProcessedAsync(string key, CancellationToken ct)
    {
        // Guard against duplicate inserts within the same unit of work.
        if (await _db.ProcessedEvents.AnyAsync(p => p.Key == key, ct))
            return;

        await _db.ProcessedEvents.AddAsync(
            new ProcessedEvent { Key = key, ProcessedAtUtc = DateTime.UtcNow }, ct);
    }
}
