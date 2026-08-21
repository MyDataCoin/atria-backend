using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an operator approves an application and the investment becomes Active.</summary>
public sealed record InvestmentActivatedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    long TokenCount,
    decimal Amount) : DomainEventBase;
