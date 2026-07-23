namespace Atria.Domain.Holders;

/// <summary>
/// Why a holder snapshot was taken. The purpose is recorded on the immutable snapshot so a payout run
/// and a regulatory statement drawn on the same date are distinguishable and independently auditable.
/// </summary>
public enum SnapshotPurpose
{
    /// <summary>Frozen holder set a distribution is computed against.</summary>
    Payout = 0,

    /// <summary>Statement of the holder register for the regulator / reporting.</summary>
    Reporting = 1
}
