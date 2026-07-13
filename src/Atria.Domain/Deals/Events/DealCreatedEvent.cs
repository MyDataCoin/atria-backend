using Atria.Domain.Common;

namespace Atria.Domain.Deals.Events;

/// <summary>Raised when a realtor creates a referral deal for a property.</summary>
public sealed record DealCreatedEvent(
    Guid DealId,
    Guid RealtorId,
    Guid PropertyId,
    decimal CommissionPercent) : DomainEventBase;
