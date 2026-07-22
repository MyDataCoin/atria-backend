using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an investor submits an offering application (Reserved), holding tokens from the pool.</summary>
public sealed record InvestmentCreatedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    decimal Amount) : DomainEventBase;
