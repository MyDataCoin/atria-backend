namespace Atria.Domain.Compliance;

/// <summary>
/// Lifecycle of a reliable on-chain operation (allowlist add/remove, token
/// allocation, anchoring). Persisted so operations can be retried and reconciled.
/// </summary>
public enum BlockchainOperationStatus
{
    Created = 0,
    Submitted = 1,
    Confirmed = 2,
    Failed = 3
}
