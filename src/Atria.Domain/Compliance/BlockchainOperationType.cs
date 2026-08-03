namespace Atria.Domain.Compliance;

/// <summary>Kinds of on-chain operations the platform issues.</summary>
public enum BlockchainOperationType
{
    AllowlistAdd = 0,
    AllowlistRemove = 1,
    TokenAllocation = 2,
    AnchorMerkleRoot = 3,

    /// <summary>Suspend every token operation on the issue's contract (draft Decree, ch. 8).</summary>
    TokenPause = 4,

    /// <summary>Resume operations after a suspension.</summary>
    TokenUnpause = 5,

    /// <summary>
    /// Burn shares from a holder's address — the withdrawal from circulation an annulment or an
    /// invalidated issue requires (ch. 11, §73). Compliance-gated on the contract.
    /// </summary>
    TokenBurn = 6,

    /// <summary>
    /// Lower the issue size on the contract after part of it is annulled, so the cap keeps matching
    /// the registered issue.
    /// </summary>
    TokenReduceSupply = 7
}
