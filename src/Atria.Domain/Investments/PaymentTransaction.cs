using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A single payment attempt against an <see cref="Investment"/>. Child entity (not an
/// aggregate root): created and owned by the Investment aggregate.
/// </summary>
public sealed class PaymentTransaction : Entity
{
    public Guid InvestmentId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public PaymentProviderType Provider { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? ExternalPaymentId { get; private set; }
    public string? FailureReason { get; private set; }

    // private ctor: creation only through the factory methods below
    private PaymentTransaction() { }

    /// <summary>Builds a successfully completed payment transaction.</summary>
    public static PaymentTransaction Completed(
        Guid investmentId, PaymentProviderType p, string extId, decimal amount, string currency)
        => new()
        {
            Id = Guid.NewGuid(),
            InvestmentId = investmentId,
            Provider = p,
            ExternalPaymentId = extId,
            Amount = amount,
            Currency = currency,
            Status = PaymentStatus.Completed
        };

    /// <summary>Builds a failed payment transaction carrying the failure reason.</summary>
    public static PaymentTransaction Failed(Guid investmentId, PaymentProviderType p, string reason)
        => new()
        {
            Id = Guid.NewGuid(),
            InvestmentId = investmentId,
            Provider = p,
            FailureReason = reason,
            Amount = 0m,
            Currency = string.Empty,
            Status = PaymentStatus.Failed
        };
}
