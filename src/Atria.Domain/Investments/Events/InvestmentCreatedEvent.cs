using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an investor creates an Investment (PendingPayment).</summary>
public sealed record InvestmentCreatedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    decimal Amount) : DomainEventBase;
