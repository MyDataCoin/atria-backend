using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when a payment for an investment completes successfully.</summary>
public sealed record PaymentCompletedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    decimal Amount,
    string ExternalPaymentId) : DomainEventBase;
