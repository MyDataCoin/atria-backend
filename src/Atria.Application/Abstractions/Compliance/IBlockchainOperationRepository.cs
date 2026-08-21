using Atria.Domain.Compliance;

namespace Atria.Application.Abstractions;

/// <summary>
/// Reads the on-chain operation queue for the operator's screen, and fetches one operation to retry.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="IBlockchainOperationQueue"/>: that one exists so a use case
/// can enqueue work without knowing anything about how it is stored, and giving it read methods
/// would let any handler start querying the queue's internals. This is the operator's view.
/// </remarks>
public interface IBlockchainOperationRepository
{
    /// <summary>
    /// The most recent operations, newest first, optionally narrowed to one status.
    /// </summary>
    Task<IReadOnlyList<BlockchainOperation>> ListAsync(
        BlockchainOperationStatus? status, int limit, CancellationToken ct);

    /// <summary>One operation by id, or null.</summary>
    Task<BlockchainOperation?> GetByIdAsync(Guid id, CancellationToken ct);

    void Update(BlockchainOperation operation);
}
