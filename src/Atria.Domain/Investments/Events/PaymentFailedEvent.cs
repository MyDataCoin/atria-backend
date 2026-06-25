using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when a payment for an investment fails.</summary>
public sealed record PaymentFailedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    string Reason) : DomainEventBase;
