namespace Atria.Domain.Investments.States;

/// <summary>
/// State pattern for an <see cref="Investment"/> (EF-friendly variant). State objects
/// are stateless singletons; the transition methods encapsulate the allowed moves and
/// their side effects (events + child payment transactions) and return the next state.
/// </summary>
public interface IInvestmentState
{
    InvestmentStatus Status { get; }

    /// <summary>PendingPayment -> Active: records a Completed payment and raises payment/activation events.</summary>
    IInvestmentState ConfirmPayment(
        Investment investment, PaymentProviderType provider, string externalPaymentId, decimal amount, string currency);

    /// <summary>PendingPayment -> Failed: records a Failed payment and raises a payment-failed event.</summary>
    IInvestmentState FailPayment(Investment investment, PaymentProviderType provider, string reason);
}
