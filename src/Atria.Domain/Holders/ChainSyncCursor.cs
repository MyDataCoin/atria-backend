using Atria.Domain.Common;

namespace Atria.Domain.Holders;

/// <summary>
/// How far the holder registry has been replayed from an issue's token contract.
/// </summary>
/// <remarks>
/// The cursor is what makes the sync resumable and idempotent: it records the last block whose
/// transfers were applied, so a restart picks up where it left off instead of replaying history and
/// double-counting every movement.
///
/// It only ever advances to a depth that is already final. Applying a transfer from the newest block
/// would mean undoing it after a reorg, and a registry that has to walk backwards is one nobody can
/// take a snapshot from.
/// </remarks>
public sealed class ChainSyncCursor : AggregateRoot
{
    /// <summary>The issue whose contract is being replayed.</summary>
    public Guid PropertyId { get; private set; }

    /// <summary>Last block whose transfers were applied. Zero means nothing has been replayed yet.</summary>
    public long LastProcessedBlock { get; private set; }

    /// <summary>When the cursor last moved.</summary>
    public DateTime LastSyncedAtUtc { get; private set; }

    private ChainSyncCursor() { }

    public static ChainSyncCursor StartFor(Guid propertyId, long fromBlock, DateTime nowUtc)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("PropertyId is required.");
        if (fromBlock < 0)
            throw new DomainException("Block number cannot be negative.");

        return new ChainSyncCursor
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            LastProcessedBlock = fromBlock,
            LastSyncedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Moves the cursor forward. Never backwards: rewinding would replay transfers that were already
    /// applied, and every one of them would be counted twice.
    /// </summary>
    public void AdvanceTo(long block, DateTime nowUtc)
    {
        if (block < LastProcessedBlock)
            throw new DomainException(
                $"The sync cursor cannot move backwards (from {LastProcessedBlock} to {block}).");

        LastProcessedBlock = block;
        LastSyncedAtUtc = nowUtc;
    }
}
