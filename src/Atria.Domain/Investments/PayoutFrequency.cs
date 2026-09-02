namespace Atria.Domain.Investments;

/// <summary>
/// How often an issue distributes to its holders.
/// </summary>
/// <remarks>
/// Not the same thing as the regulator's reporting calendar in
/// <see cref="Atria.Domain.Regulatory.RegulatoryReportKind"/>, which also has monthly and quarterly
/// obligations. That is what the platform owes the regulator; this is what the issue owes its
/// investors, and the two periods do not have to line up.
/// </remarks>
public enum PayoutFrequency
{
    /// <summary>Not stated. The default, and what every pre-existing issue reads as.</summary>
    Unspecified = 0,

    /// <summary>
    /// No distributions at all yet — the object does not earn. What a building under construction
    /// is, and the honest answer for the first issue until it is commissioned.
    /// </summary>
    None = 1,

    Monthly = 2,
    Quarterly = 3,
    Annually = 4
}
