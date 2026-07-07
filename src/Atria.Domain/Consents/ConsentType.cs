namespace Atria.Domain.Consents;

/// <summary>Kinds of legal consent an investor can accept. Sent/returned by name.</summary>
public enum ConsentType
{
    /// <summary>Personal-data processing notice (ПДН). Required before KYC.</summary>
    Pdn = 0
}
