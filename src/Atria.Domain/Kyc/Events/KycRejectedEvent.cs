using Atria.Domain.Common;

namespace Atria.Domain.Kyc.Events;

/// <summary>Raised when a KYC profile is rejected, carrying the rejection reason.</summary>
public sealed record KycRejectedEvent(Guid KycProfileId, Guid UserId, string Reason)
    : DomainEventBase;
