using Atria.Domain.Common;

namespace Atria.Domain.Consents;

/// <summary>
/// Single source of truth for the CURRENT required version of each consent type.
/// Enforcement (e.g. KYC submit) compares an investor's recorded acceptance against
/// this. Bump the version here when the consent text changes to force re-acceptance.
/// </summary>
public static class ConsentPolicy
{
    public static string CurrentVersion(ConsentType type) => type switch
    {
        ConsentType.Pdn => "1.0",
        _ => throw new DomainException($"No consent policy configured for {type}.")
    };
}
