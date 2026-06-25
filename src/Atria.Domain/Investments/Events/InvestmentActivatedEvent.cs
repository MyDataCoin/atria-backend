using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an investment becomes Active after a confirmed payment.</summary>
public sealed record InvestmentActivatedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    decimal Amount) : DomainEventBase;
