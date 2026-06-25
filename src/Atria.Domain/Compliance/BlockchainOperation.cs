using Atria.Domain.Common;

namespace Atria.Domain.Compliance;

/// <summary>
/// A reliable, retryable on-chain operation (allowlist add/remove, token allocation,
/// Merkle-root anchoring). Persists status + attempts so the worker can submit,
/// reconcile and retry idempotently. Lifecycle: Created -> Submitted -> Confirmed/Failed.
/// </summary>
public sealed class BlockchainOperation : AggregateRoot
{
    public BlockchainOperationType Type { get; private set; }
    public string Payload { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public BlockchainOperationStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? TransactionRef { get; private set; }
    public string? Error { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }

    // EF / factory only.
    private BlockchainOperation() { }

    /// <summary>Creates a pending operation in the <see cref="BlockchainOperationStatus.Created"/> state.</summary>
    public static BlockchainOperation Create(BlockchainOperationType type, string payload, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new DomainException("Operation payload is required.");

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("Idempotency key is required.");

        return new BlockchainOperation
        {
            Id = Guid.NewGuid(),
            Type = type,
            Payload = payload,
            IdempotencyKey = idempotencyKey,
            Status = BlockchainOperationStatus.Created
        };
    }

    /// <summary>Records that the operation was submitted to the chain with the given tx reference.</summary>
    public void MarkSubmitted(string txRef)
    {
        if (string.IsNullOrWhiteSpace(txRef))
            throw new DomainException("Transaction reference is required.");

        // Submittable from Created, or re-submittable after a transient failure.
        if (Status is BlockchainOperationStatus.Confirmed)
            throw new InvalidStateTransitionException("Cannot submit an already confirmed operation.");

        Status = BlockchainOperationStatus.Submitted;
        TransactionRef = txRef;
        Error = null;
    }

    /// <summary>Marks the operation confirmed on chain.</summary>
    public void MarkConfirmed()
    {
        if (Status is not BlockchainOperationStatus.Submitted)
            throw new InvalidStateTransitionException("Only a submitted operation can be confirmed.");

        Status = BlockchainOperationStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        Error = null;
    }

    /// <summary>Marks the operation failed and stores the error for diagnostics/retry.</summary>
    public void MarkFailed(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new DomainException("Failure error is required.");

        if (Status is BlockchainOperationStatus.Confirmed)
            throw new InvalidStateTransitionException("Cannot fail an already confirmed operation.");

        Status = BlockchainOperationStatus.Failed;
        Error = error;
    }

    /// <summary>Increments the retry counter before the next submission attempt.</summary>
    public void IncrementAttempt() => Attempts++;
}
