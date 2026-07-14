using Atria.Domain.Common;

namespace Atria.Domain.Deals.Events;

/// <summary>Raised when a deal's referral link expires unused and the deal is rejected.</summary>
public sealed record DealRejectedEvent(
    Guid DealId,
    Guid RealtorId,
    Guid PropertyId,
    decimal CommissionPercent) : DomainEventBase;
