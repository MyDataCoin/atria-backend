using Atria.Domain.Holders;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="HolderSnapshot"/>, the frozen historical view of an issuance's
/// holder register. Snapshots are only ever written once and read afterwards — there is no update path.
/// </summary>
public interface IHolderSnapshotRepository : IRepository<HolderSnapshot>
{
    /// <summary>
    /// The snapshot already taken for this issuance, cut and purpose, or null if there is none. Lets
    /// snapshot creation stay idempotent: asking twice for the same cut returns the first snapshot
    /// rather than building a second one from since-changed holdings.
    /// </summary>
    Task<HolderSnapshot?> FindByCutAsync(
        Guid propertyId, DateTime snapshotAtUtc, SnapshotPurpose purpose, CancellationToken ct);

    /// <summary>A snapshot with all of its rows loaded, or null. Read-only.</summary>
    Task<HolderSnapshot?> GetWithRowsAsync(Guid id, CancellationToken ct);

    /// <summary>Snapshot headers of an issuance, newest cut first. Rows are not loaded.</summary>
    Task<IReadOnlyList<HolderSnapshot>> ListByPropertyAsync(Guid propertyId, CancellationToken ct);
}
