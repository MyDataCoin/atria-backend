using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an investor cancels their offering application; its reserved tokens are returned.</summary>
public sealed record InvestmentCancelledEvent(
    Guid InvestmentId,
    Guid InvestorId) : DomainEventBase;
