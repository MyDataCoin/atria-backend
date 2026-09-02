namespace Atria.Domain.Investments;

/// <summary>
/// What a property image actually is.
/// </summary>
/// <remarks>
/// This exists for disclosure, not for tidiness. The first object is a land plot under design: there
/// is nothing to photograph, and the management company is supplying renders from the project
/// documentation. A render shown without saying so is a picture of a building that does not exist,
/// presented to someone deciding whether to put money into it — so the kind travels with the image
/// and the offering page says it out loud.
/// <para>
/// Persisted as an int; the wire form is the lowercase snake_case name.
/// </para>
/// </remarks>
public enum PropertyImageKind
{
    /// <summary>A photograph of something that exists.</summary>
    Photo = 0,

    /// <summary>An architectural visualisation. Not a photograph, and labelled as such wherever it is shown.</summary>
    Render = 1,

    /// <summary>A floor plan or layout drawing.</summary>
    FloorPlan = 2,

    /// <summary>A site or master plan.</summary>
    SitePlan = 3
}
