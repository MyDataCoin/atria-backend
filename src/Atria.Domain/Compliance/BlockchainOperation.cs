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

    /// <summary>
    /// Confirmations counted on the submitted transaction at the last reconciliation pass, or null
    /// while nothing has been observed yet.
    /// </summary>
    /// <remarks>
    /// Recorded because "Submitted" on its own tells an operator nothing about whether a run is
    /// progressing or wedged: a transaction three blocks deep and one that never left the mempool
    /// look identical. The count is what distinguishes them on the queue screen.
    /// </remarks>
    public long? Confirmations { get; private set; }

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
        // A resubmission starts counting again: the depth that was observed belonged to the previous
        // transaction, and carrying it over would report the new one as further along than it is.
        Confirmations = null;
        Error = null;
    }

    /// <summary>
    /// Records how deep the submitted transaction is without settling the operation. Called on every
    /// reconciliation pass while the transaction is still short of the required depth.
    /// </summary>
    public void ObserveConfirmations(long confirmations)
    {
        if (confirmations < 0)
            throw new DomainException("Confirmation count cannot be negative.");

        Confirmations = confirmations;
    }

    /// <summary>Marks the operation confirmed on chain.</summary>
    public void MarkConfirmed(long confirmations)
    {
        if (Status is not BlockchainOperationStatus.Submitted)
            throw new InvalidStateTransitionException("Only a submitted operation can be confirmed.");

        Status = BlockchainOperationStatus.Confirmed;
        ConfirmedAtUtc = DateTime.UtcNow;
        Confirmations = confirmations;
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

    /// <summary>
    /// Puts a failed operation back in the queue for another submission attempt.
    /// </summary>
    /// <remarks>
    /// The attempt counter is deliberately NOT reset: it is the record of how many times this piece
    /// of work has been tried, and zeroing it would hide an operation that keeps failing behind a
    /// clean-looking row. The idempotency key stays too, so a retry cannot become a second mint.
    /// </remarks>
    public void Requeue()
    {
        if (Status is not BlockchainOperationStatus.Failed)
            throw new InvalidStateTransitionException("Only a failed operation can be re-queued.");

        Status = BlockchainOperationStatus.Created;
        TransactionRef = null;
        Confirmations = null;
        Error = null;
    }

    /// <summary>Increments the retry counter before the next submission attempt.</summary>
    public void IncrementAttempt() => Attempts++;
}
