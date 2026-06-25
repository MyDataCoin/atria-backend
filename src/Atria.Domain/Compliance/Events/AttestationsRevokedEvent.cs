using Atria.Domain.Common;

namespace Atria.Domain.Compliance.Events;

/// <summary>Raised when an investor's attestations are revoked (e.g. KYC rejection).</summary>
public sealed record AttestationsRevokedEvent(Guid InvestorId, string Reason) : DomainEventBase;
