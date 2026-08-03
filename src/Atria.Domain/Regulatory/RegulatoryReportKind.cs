namespace Atria.Domain.Regulatory;

/// <summary>
/// The notifications the draft Decree requires an issuer to file, each with its own deadline. Every
/// one of them is derived from data the platform already holds, which is why they are generated here
/// rather than typed up by hand.
/// </summary>
public enum RegulatoryReportKind
{
    /// <summary>
    /// Funds landed on the issuer's account and the matching number of tokens was issued.
    /// Due 5 working days after the event (§24). Raised per issue, per event.
    /// </summary>
    FundsReceivedAndIssued = 0,

    /// <summary>
    /// Amount actually placed over the month and payment of the registration fee.
    /// Due 10 working days after the month ends (§49).
    /// </summary>
    MonthlyPlacement = 1,

    /// <summary>
    /// Results of the issue and of the public placement, on the form of annex 6.
    /// Due 5 working days after the sale completes (§§50, 52).
    /// </summary>
    PlacementResults = 2,

    /// <summary>
    /// Interim collateral report between audits, on the form of annex 8. Monthly (§15).
    /// </summary>
    CollateralInterim = 3,

    /// <summary>
    /// Anti-money-laundering report. Due 15 working days after the quarter ends (§80).
    /// </summary>
    AmlQuarterly = 4
}
