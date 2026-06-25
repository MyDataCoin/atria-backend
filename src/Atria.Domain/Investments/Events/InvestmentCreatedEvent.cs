using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an Investment is created from an approved application (PendingPayment).</summary>
public sealed record InvestmentCreatedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    decimal Amount,
    Guid ApplicationId) : DomainEventBase;
