using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>
/// Raised when a holder exercises the 14-day right of withdrawal (draft Decree, §44). The shares
/// return to the pool, anything already minted is burned, and the money is owed back.
/// </summary>
public sealed record InvestmentWithdrawnEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    long TokenCount,
    decimal Amount,
    string Currency) : DomainEventBase;
