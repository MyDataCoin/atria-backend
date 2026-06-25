using Atria.Domain.Common;

namespace Atria.Domain.Kyc.Events;

/// <summary>
/// Raised when a KYC profile is approved. Carries the wallet address so the
/// Compliance module can allowlist it without re-reading the aggregate.
/// </summary>
public sealed record KycApprovedEvent(Guid KycProfileId, Guid UserId, string? WalletAddress)
    : DomainEventBase;
