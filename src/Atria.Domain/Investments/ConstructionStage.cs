namespace Atria.Domain.Investments;

/// <summary>
/// How far along the physical object is. Deliberately separate from <see cref="PropertyStatus"/>:
/// that tracks the <em>placement</em> (draft, announced, open, completed), this tracks the
/// <em>building</em>. The two move independently — an issue can be open for subscription while the
/// site is still a design on paper, which is exactly the case for the first object.
/// </summary>
/// <remarks>
/// Persisted as an int; the wire form is the lowercase snake_case name.
/// </remarks>
public enum ConstructionStage
{
    /// <summary>Not stated. The default, and what every pre-existing issue reads as.</summary>
    Unspecified = 0,

    /// <summary>Nothing is built and nothing is being built: bare land, no design work yet.</summary>
    LandOnly = 1,

    /// <summary>Design and permitting. No construction on site.</summary>
    Design = 2,

    /// <summary>Construction under way.</summary>
    UnderConstruction = 3,

    /// <summary>Built and commissioned — the point from which rent, and so distributions, can exist.</summary>
    Commissioned = 4
}
