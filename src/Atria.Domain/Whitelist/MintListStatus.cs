namespace Atria.Domain.Whitelist;

/// <summary>Lifecycle of a mint list — a batch of whitelisted requests handed to the exchange.</summary>
public enum MintListStatus
{
    /// <summary>Assembled but not handed over yet; still editable by being called off and rebuilt.</summary>
    Draft = 0,

    /// <summary>Handed to the exchange. The rows are fixed from here on — someone else is acting on them.</summary>
    Sent = 1,

    /// <summary>The exchange minted the batch; every request in it is <see cref="WhitelistStatus.Minted"/>.</summary>
    Executed = 2,

    /// <summary>Called off before execution; its requests return to the mintable pool.</summary>
    Cancelled = 3
}
