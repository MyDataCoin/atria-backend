namespace Atria.Domain.Holders;

/// <summary>
/// Where a holder position's token count was last established. The chain is the ultimate source of
/// truth once chain reading is wired (see the registry reconciliation); until then the registry is
/// projected from our own Active investment records.
/// </summary>
public enum HolderSource
{
    /// <summary>Derived from our own Active investment records (no chain read yet).</summary>
    OurRecords = 0,

    /// <summary>Read from the token contract on chain (authoritative).</summary>
    Chain = 1,

    /// <summary>Reported by the licensed trading operator (secondary market settlement).</summary>
    TradingOperator = 2
}
