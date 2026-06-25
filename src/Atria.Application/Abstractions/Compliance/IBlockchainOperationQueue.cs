using Atria.Domain.Compliance;

namespace Atria.Application.Abstractions;

/// <summary>
/// Enqueues an on-chain operation as a durable, idempotent record. A background
/// worker (Infrastructure) sends it via <see cref="IBlockchainSigner"/>, tracks
/// transaction status and reconciles confirmation. A retry never sends twice
/// (idempotency key).
/// </summary>
public interface IBlockchainOperationQueue
{
    Task EnqueueAsync(
        BlockchainOperationType type,
        string payload,
        string idempotencyKey,
        CancellationToken ct);
}
