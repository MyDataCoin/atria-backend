using Atria.Domain.Common;

namespace Atria.Domain.Kyc.Events;

/// <summary>Raised when a KYC profile is submitted and moves to UnderReview.</summary>
public sealed record KycSubmittedEvent(Guid KycProfileId, Guid UserId) : DomainEventBase;
