namespace Atria.Domain.Whitelist;

/// <summary>
/// Where a purchase request stands on its way to being minted. Distinct from the on-chain allowlist
/// (<c>Allowlist.sol</c>, driven by the Compliance module): this is the operator's working queue of
/// requests, the allowlist is the contract's own record of permitted addresses.
/// </summary>
public enum WhitelistStatus
{
    /// <summary>The investor applied; the request is waiting for an operator's decision.</summary>
    Pending = 0,

    /// <summary>
    /// The application was approved and the request can go into a mint list. A request with no wallet
    /// address on it stays here too — it is approved, but there is nothing to mint to yet.
    /// </summary>
    Ready = 1,

    /// <summary>Included in a mint list. It leaves the pool of mintable requests until that list is called off.</summary>
    Batched = 2,

    /// <summary>The mint list it belonged to was executed — the shares exist on chain.</summary>
    Minted = 3,

    /// <summary>
    /// The request left the queue without being minted: the application was rejected, cancelled,
    /// expired, or the holder walked away inside the 14-day window.
    /// </summary>
    Excluded = 4
}
