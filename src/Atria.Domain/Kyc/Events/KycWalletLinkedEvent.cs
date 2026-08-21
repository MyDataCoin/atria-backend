using Atria.Domain.Common;

namespace Atria.Domain.Kyc.Events;

/// <summary>
/// Raised when an investor links a wallet to an already-verified KYC profile.
/// </summary>
/// <remarks>
/// By design the wallet is collected AFTER verification (see KycController), so approval frequently
/// happens with no address at all. Everything downstream — the compliance profile, and through it the
/// whitelist queue — took its copy of the address from <c>KycApprovedEvent</c> and had no way to hear
/// about one that arrived later: the request stayed marked "no wallet" forever and could never be
/// batched. This event is that missing notification.
/// </remarks>
public sealed record KycWalletLinkedEvent(Guid KycProfileId, Guid UserId, string WalletAddress)
    : DomainEventBase;
