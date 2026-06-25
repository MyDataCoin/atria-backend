namespace Atria.Domain.Compliance;

/// <summary>
/// Verifiable attestation kinds issued to an investor's DID. The string wire
/// values (used by the Tessera facades) live in <see cref="AttestationTypes"/>.
/// Identity data itself is NEVER written on chain — only attestation roots are.
/// </summary>
public enum AttestationType
{
    KycVerified = 0,
    Resident = 1,
    PhoneVerified = 2
}

/// <summary>Canonical wire strings for attestation types.</summary>
public static class AttestationTypes
{
    public const string KycVerified = "kyc_verified";
    public const string Resident = "resident";
    public const string PhoneVerified = "phone_verified";

    public static string ToWire(this AttestationType type) => type switch
    {
        AttestationType.KycVerified => KycVerified,
        AttestationType.Resident => Resident,
        AttestationType.PhoneVerified => PhoneVerified,
        _ => type.ToString()
    };
}
