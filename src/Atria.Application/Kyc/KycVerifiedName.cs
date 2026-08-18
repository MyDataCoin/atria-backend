using Atria.Application.Abstractions;

namespace Atria.Application.Kyc;

/// <summary>
/// Turns a provider's verified identity into the single name the KYC record carries.
/// </summary>
/// <remarks>
/// Shared by every path that can approve a profile — the webhook and the operator's pull. Kept in one
/// place because the paths must not disagree: a profile approved one way and a profile approved the
/// other have to end up with the same name written the same way.
/// </remarks>
public static class KycVerifiedName
{
    /// <summary>Prefers a single verified full name; otherwise joins the split first/last parts.</summary>
    public static string? Compose(KycVerifiedIdentity? identity)
    {
        if (identity is null)
            return null;

        if (!string.IsNullOrWhiteSpace(identity.FullName))
            return identity.FullName.Trim();

        var joined = string.Join(' ', new[] { identity.FirstName, identity.LastName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim()));

        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }
}
