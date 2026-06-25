using Atria.Application.Abstractions;
using Atria.Domain.Compliance;
using Atria.Infrastructure.Configuration;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Background worker that drains queued <see cref="BlockchainOperation"/> records:
/// builds a <see cref="SigningRequest"/>, submits via <see cref="IBlockchainSigner"/>
/// and tracks status. A reconciliation pass marks submitted operations confirmed.
/// Idempotent: a status guard ensures an operation is never sent twice.
/// </summary>
public sealed class BlockchainOperationWorker : BackgroundService
{
    private readonly int _maxAttempts;
    private readonly int _batchSize;
    private readonly TimeSpan _pollInterval;
    private readonly bool _autoConfirmSubmitted;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BlockchainOperationWorker> _logger;

    public BlockchainOperationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BlockchainOptions> options,
        ILogger<BlockchainOperationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var opts = options.Value;
        _maxAttempts = opts.MaxAttempts;
        _batchSize = opts.BatchSize;
        _pollInterval = TimeSpan.FromSeconds(opts.PollSeconds);
        _autoConfirmSubmitted = opts.AutoConfirmSubmitted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // A fresh scope per iteration so scoped services (DbContext) are not reused.
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AtriaDbContext>();
                var signer = scope.ServiceProvider.GetRequiredService<IBlockchainSigner>();

                await SubmitPendingAsync(context, signer, stoppingToken);
                await ReconcileSubmittedAsync(context, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blockchain operation worker iteration failed.");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Submits operations still in Created/Failed (under the attempt cap) to the signer.</summary>
    private async Task SubmitPendingAsync(AtriaDbContext context, IBlockchainSigner signer, CancellationToken ct)
    {
        var pending = await context.Set<BlockchainOperation>()
            .Where(o => (o.Status == BlockchainOperationStatus.Created
                         || o.Status == BlockchainOperationStatus.Failed)
                        && o.Attempts < _maxAttempts)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var operation in pending)
        {
            // Status guard: skip anything that is no longer submittable (idempotency).
            if (operation.Status is BlockchainOperationStatus.Submitted or BlockchainOperationStatus.Confirmed)
                continue;

            operation.IncrementAttempt();

            try
            {
                var request = new SigningRequest(
                    OperationType: operation.Type.ToString(),
                    UnsignedPayload: operation.Payload,
                    ChainId: null);

                var result = await signer.SignAndSubmitAsync(request, ct);
                var txRef = result.SubmissionReference ?? result.SignedPayload;

                operation.MarkSubmitted(txRef);

                _logger.LogInformation(
                    "Submitted blockchain operation {OperationId} ({Type}); tx {TransactionRef}.",
                    operation.Id, operation.Type, txRef);
            }
            catch (Exception ex)
            {
                operation.MarkFailed(ex.Message);
                _logger.LogWarning(ex,
                    "Blockchain operation {OperationId} ({Type}) failed on attempt {Attempts}.",
                    operation.Id, operation.Type, operation.Attempts);
            }
        }

        if (pending.Count > 0)
            await context.SaveChangesAsync(ct);
    }

    /// <summary>Reconciliation pass: marks submitted operations as confirmed once finalized.</summary>
    private async Task ReconcileSubmittedAsync(AtriaDbContext context, CancellationToken ct)
    {
        // Finality is gated by configuration. For the pilot AutoConfirmSubmitted is true,
        // so a submitted operation is treated as confirmed. When false, operations stay
        // Submitted until a real finality check confirms them.
        // NOTE: a real implementation MUST query the chain (or IChainAnchor / signer
        // service) for the receipt of operation.TransactionRef and only confirm on
        // finality. This pilot path does NOT make any chain call.
        if (!_autoConfirmSubmitted)
            return;

        var submitted = await context.Set<BlockchainOperation>()
            .Where(o => o.Status == BlockchainOperationStatus.Submitted)
            .OrderBy(o => o.CreatedAtUtc)
            .Take(_batchSize)
            .ToListAsync(ct);

        foreach (var operation in submitted)
        {
            operation.MarkConfirmed();

            _logger.LogInformation(
                "Confirmed blockchain operation {OperationId} ({Type}); tx {TransactionRef}.",
                operation.Id, operation.Type, operation.TransactionRef);
        }

        if (submitted.Count > 0)
            await context.SaveChangesAsync(ct);
    }
}
