using Atria.Domain.Common;

namespace Atria.Domain.Deals.Events;

/// <summary>Raised when an investor buys through a deal's referral link, closing it successfully.</summary>
public sealed record DealSucceededEvent(
    Guid DealId,
    Guid RealtorId,
    Guid PropertyId,
    Guid InvestmentId,
    decimal CommissionPercent) : DomainEventBase;
