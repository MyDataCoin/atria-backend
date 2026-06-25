using Atria.Domain.Common;

namespace Atria.Domain.Compliance.Events;

/// <summary>
/// Raised when an investor's wallet is added to or removed from the permissioned
/// allowlist. <paramref name="Added"/> is true on add, false on remove.
/// </summary>
public sealed record AllowlistUpdatedEvent(Guid InvestorId, string WalletAddress, bool Added) : DomainEventBase;
