namespace Atria.Domain.Regulatory;

/// <summary>Where a required notification stands against its deadline.</summary>
public enum RegulatoryReportStatus
{
    /// <summary>The obligation exists and its content has not been produced yet.</summary>
    Due = 0,

    /// <summary>The document has been generated from system data and is ready to file.</summary>
    Generated = 1,

    /// <summary>Filed with the regulator; the reference is recorded on the report.</summary>
    Filed = 2
}
