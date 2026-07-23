using Atria.Domain.Common;

namespace Atria.Domain.Holders;

/// <summary>
/// One address line of a <see cref="HolderSnapshot"/>. Immutable, like its parent: a snapshot is a
/// frozen historical record, never edited after creation.
/// </summary>
public sealed class HolderSnapshotRow : Entity
{
    /// <summary>The owning snapshot.</summary>
    public Guid SnapshotId { get; private set; }

    /// <summary>The holding address at snapshot time.</summary>
    public string WalletAddress { get; private set; } = null!;

    /// <summary>Shares held on the address at snapshot time.</summary>
    public long TokenCount { get; private set; }

    /// <summary>The investor behind the address at snapshot time, if resolved. May be null.</summary>
    public Guid? InvestorId { get; private set; }

    /// <summary>
    /// The address's fraction of the offering at snapshot time: <see cref="TokenCount"/> over the
    /// snapshot's total. A reproducible number a proportional payout is computed against.
    /// </summary>
    public decimal Share { get; private set; }

    private HolderSnapshotRow() { }

    // Built by HolderSnapshot.Create (same assembly) as part of an atomic, immutable snapshot.
    internal static HolderSnapshotRow Create(
        Guid snapshotId, string walletAddress, long tokenCount, Guid? investorId, decimal share)
        => new()
        {
            Id = Guid.NewGuid(),
            SnapshotId = snapshotId,
            WalletAddress = walletAddress,
            TokenCount = tokenCount,
            InvestorId = investorId,
            Share = share
        };
}
