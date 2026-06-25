namespace Atria.Domain.Compliance;

/// <summary>Kinds of on-chain operations the platform issues.</summary>
public enum BlockchainOperationType
{
    AllowlistAdd = 0,
    AllowlistRemove = 1,
    TokenAllocation = 2,
    AnchorMerkleRoot = 3
}
