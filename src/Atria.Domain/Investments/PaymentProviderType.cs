namespace Atria.Domain.Investments;

/// <summary>
/// Payment providers. Selected at runtime through DI by type (Strategy), never via if/else.
/// </summary>
public enum PaymentProviderType
{
    Stripe = 0,
    BankTransfer = 1
}
