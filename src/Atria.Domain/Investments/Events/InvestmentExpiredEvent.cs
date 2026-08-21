using Atria.Domain.Common;

namespace Atria.Domain.Investments.Events;

/// <summary>
/// Raised when a Reserved application's reservation window lapses without operator approval; its
/// reserved tokens are returned to the pool. Emitted by the background reservation-expiry sweep.
/// </summary>
public sealed record InvestmentExpiredEvent(
    Guid InvestmentId,
    Guid InvestorId,
    Guid PropertyId,
    long TokenCount) : DomainEventBase;
