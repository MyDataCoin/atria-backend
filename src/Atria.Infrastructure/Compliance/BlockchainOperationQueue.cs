using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// <see cref="IBlockchainOperationQueue"/> that persists a <see cref="BlockchainOperation"/>
/// for the background worker to send. Enqueue is idempotent: if an operation with the
/// same idempotency key already exists, it is a no-op (no duplicate on-chain effect).
/// </summary>
public sealed class BlockchainOperationQueue : IBlockchainOperationQueue
{
    private readonly IRepository<BlockchainOperation> _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AtriaDbContext _context;
    private readonly ILogger<BlockchainOperationQueue> _logger;

    public BlockchainOperationQueue(
        IRepository<BlockchainOperation> repository,
        IUnitOfWork unitOfWork,
        AtriaDbContext context,
        ILogger<BlockchainOperationQueue> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _context = context;
        _logger = logger;
    }

    public async Task EnqueueAsync(
        BlockchainOperationType type,
        string payload,
        string idempotencyKey,
        CancellationToken ct)
    {
        // Idempotent enqueue: skip if an op with the same key is already queued.
        var exists = await _context.Set<BlockchainOperation>()
            .AsNoTracking()
            .AnyAsync(o => o.IdempotencyKey == idempotencyKey, ct);

        if (exists)
        {
            _logger.LogDebug(
                "Blockchain operation {Type} with key {IdempotencyKey} already queued; skipping.",
                type, idempotencyKey);
            return;
        }

        var operation = BlockchainOperation.Create(type, payload, idempotencyKey);
        await _repository.AddAsync(operation, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Enqueued blockchain operation {Type} with key {IdempotencyKey}.",
            type, idempotencyKey);
    }
}
