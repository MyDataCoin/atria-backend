namespace Atria.Domain.Investments;

/// <summary>
/// What a property document is, so a reader can find the one they need.
/// </summary>
/// <remarks>
/// The request document asks for three kinds of attachment at once — a technical passport with the
/// cadastre data, photo materials and layouts, and a construction schedule. Without a category they
/// arrive as one undifferentiated pile of file names, and the lawyer who needs the cadastre extract
/// looks for it by eye.
/// <para>
/// Persisted as an int; the wire form is the lowercase snake_case name.
/// </para>
/// </remarks>
public enum PropertyDocumentCategory
{
    /// <summary>Not stated. What every document uploaded before this field is.</summary>
    Unspecified = 0,

    /// <summary>Title and legal papers — ownership, contracts, the offering memo.</summary>
    Legal = 1,

    /// <summary>Technical passport and cadastre data.</summary>
    TechnicalPassport = 2,

    /// <summary>An appraisal report.</summary>
    Valuation = 3,

    /// <summary>Collateral file — what secures the issue.</summary>
    Collateral = 4,

    /// <summary>Construction schedule and stage reports.</summary>
    ConstructionSchedule = 5,

    /// <summary>Layouts, floor schemes and other showcase material kept as a file rather than an image.</summary>
    Layout = 6
}
