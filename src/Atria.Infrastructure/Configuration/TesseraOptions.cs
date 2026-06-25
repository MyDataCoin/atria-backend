using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for the local (in-house) Tessera-style compliance service. The real
/// Tessera SDK packages are not publicly resolvable, so these drive our own
/// Issuer/Verifier behaviour: a verification policy id and the platform issuer DID.
/// </summary>
public sealed class TesseraOptions
{
    public const string SectionName = "Tessera";

    /// <summary>Verification policy id presentations are checked against.</summary>
    [Required]
    public string PolicyId { get; init; } = null!;

    /// <summary>The platform's issuer DID used to derive investor DIDs.</summary>
    [Required]
    public string IssuerDid { get; init; } = null!;
}
