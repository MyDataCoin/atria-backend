using Atria.Domain.Common;

namespace Atria.Domain.Holders;

/// <summary>
/// A frozen, immutable copy of an issuance's holder register at a moment in time. Answers "who held
/// what then", as opposed to <see cref="HolderPosition"/>'s "who holds what now". Once created it is
/// never edited: recomputing a past payout means a new snapshot, not a correction of the old one. A
/// snapshot rebuilt from the same holdings on the same cut is byte-for-byte reproducible.
/// </summary>
public sealed class HolderSnapshot : AggregateRoot
{
    /// <summary>Number of fractional digits the per-row share is rounded to.</summary>
    public const int ShareScale = 8;

    /// <summary>The issuance the snapshot covers.</summary>
    public Guid PropertyId { get; private set; }

    /// <summary>The instant the register was cut.</summary>
    public DateTime SnapshotAtUtc { get; private set; }

    /// <summary>Why the snapshot was taken.</summary>
    public SnapshotPurpose Purpose { get; private set; }

    /// <summary>
    /// The chain block height the cut corresponds to, which makes the snapshot reproducible by anyone
    /// with chain access. Null until chain reading is wired (the our-records phase).
    /// </summary>
    public long? BlockNumber { get; private set; }

    /// <summary>Total shares across all rows — the denominator every row's share is taken over.</summary>
    public long TotalTokens { get; private set; }

    /// <summary>Number of holding addresses in the snapshot.</summary>
    public int AddressCount { get; private set; }

    /// <summary>The user who created the snapshot.</summary>
    public Guid CreatedByUserId { get; private set; }

    private readonly List<HolderSnapshotRow> _rows = new();
    public IReadOnlyCollection<HolderSnapshotRow> Rows => _rows.AsReadOnly();

    private HolderSnapshot() { }

    /// <summary>
    /// Builds a snapshot from the holdings observed at the cut. Zero-balance entries are dropped, rows
    /// are ordered by address for a stable layout, and each row's share is <c>TokenCount / TotalTokens</c>
    /// rounded to <see cref="ShareScale"/> places. Header totals are derived from the rows, so the same
    /// input yields the same snapshot.
    /// </summary>
    public static HolderSnapshot Create(
        Guid propertyId, DateTime snapshotAtUtc, SnapshotPurpose purpose, long? blockNumber,
        Guid createdByUserId, IEnumerable<HolderSnapshotEntry> entries)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (createdByUserId == Guid.Empty)
            throw new DomainException("CreatedByUserId is required.");
        if (blockNumber is < 0)
            throw new DomainException("Block number cannot be negative.");

        var holdings = entries
            .Where(e => e.TokenCount > 0)
            .OrderBy(e => e.WalletAddress, StringComparer.Ordinal)
            .ToList();

        var totalTokens = holdings.Sum(e => e.TokenCount);

        var snapshot = new HolderSnapshot
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            SnapshotAtUtc = snapshotAtUtc,
            Purpose = purpose,
            BlockNumber = blockNumber,
            CreatedByUserId = createdByUserId,
            TotalTokens = totalTokens,
            AddressCount = holdings.Count
        };

        foreach (var e in holdings)
        {
            var share = totalTokens == 0
                ? 0m
                : Math.Round((decimal)e.TokenCount / totalTokens, ShareScale, MidpointRounding.ToEven);
            snapshot._rows.Add(HolderSnapshotRow.Create(snapshot.Id, e.WalletAddress, e.TokenCount, e.InvestorId, share));
        }

        return snapshot;
    }
}
