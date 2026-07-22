namespace Atria.Domain.Investments;

/// <summary>
/// On-chain confirmation status of an investment's token allocation (mint). Kept distinct from the
/// investment lifecycle: an investment is Active once approved, but its tokens are only
/// <see cref="Confirmed"/> once the mint transaction reaches finality on chain. While chain wiring is
/// disabled the status stays <see cref="None"/>.
/// </summary>
public enum OnChainStatus
{
    /// <summary>No on-chain action has been taken (chain wiring off, or not yet approved).</summary>
    None = 0,

    /// <summary>Allocation/mint submitted to the network; awaiting finality.</summary>
    Pending = 1,

    /// <summary>Mint confirmed final on chain.</summary>
    Confirmed = 2,

    /// <summary>Mint failed on chain.</summary>
    Failed = 3
}
