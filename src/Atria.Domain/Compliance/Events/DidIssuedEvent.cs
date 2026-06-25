using Atria.Domain.Common;

namespace Atria.Domain.Compliance.Events;

/// <summary>Raised when a DID has been issued for an investor's compliance profile.</summary>
public sealed record DidIssuedEvent(Guid InvestorId, string Did) : DomainEventBase;
