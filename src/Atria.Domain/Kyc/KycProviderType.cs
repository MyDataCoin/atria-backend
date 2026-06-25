namespace Atria.Domain.Kyc;

/// <summary>
/// KYC providers. Selected at runtime through DI by type (Strategy), never via if/else.
/// Didit is the primary provider for the project.
/// </summary>
public enum KycProviderType
{
    Didit = 0,
    SumSub = 1,
    Manual = 2
}
