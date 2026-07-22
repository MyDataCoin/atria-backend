using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>Raised when an operator rejects an offering application; its reserved tokens are returned.</summary>
public sealed record InvestmentRejectedEvent(
    Guid InvestmentId,
    Guid InvestorId,
    string Reason) : DomainEventBase;
