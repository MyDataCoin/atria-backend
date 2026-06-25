using System.Text.Json;
using Atria.Application.Abstractions;
using Atria.Domain.Common;
using Atria.Domain.Outbox;
using Atria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Outbox;

/// <summary>
/// Polls the transactional outbox: reads a batch of unprocessed <see cref="OutboxMessage"/>
/// rows (oldest first), deserializes each via its assembly-qualified type + System.Text.Json,
/// dispatches through <see cref="IDomainEventDispatcher"/> (at-least-once; handlers are
/// idempotent), and marks each processed/failed. A fresh DI scope is created per iteration so
/// scoped services (DbContext, dispatcher) are resolved correctly. Backs off when idle/erroring.
/// </summary>
public sealed class OutboxDispatcherBackgroundService : BackgroundService
{
    private const int BatchSize = 50;
    private const int MaxAttempts = 5;                                  // attempts cap
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherBackgroundService> _logger;

    public OutboxDispatcherBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<OutboxDispatcherBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = IdleDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Whole-batch failure (e.g. DB unreachable): log and back off, no row info leaked.
                _logger.LogError(ex, "Outbox batch processing failed; backing off.");
                delay = NextDelay(delay);
                await SafeDelay(delay, stoppingToken);
                continue;
            }

            if (processed == 0)
            {
                // Nothing to do: back off up to the cap.
                delay = NextDelay(delay);
                await SafeDelay(delay, stoppingToken);
            }
            else
            {
                // Work found: reset delay and immediately try the next batch.
                delay = IdleDelay;
            }
        }
    }

    // Processes one batch; returns how many rows were attempted. Saved via the scoped DbContext.
    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AtriaDbContext>();
        var dispatcher = sp.GetRequiredService<IDomainEventDispatcher>();

        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.OccurredOnUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (messages.Count == 0)
            return 0;

        foreach (var message in messages)
        {
            try
            {
                var domainEvent = Deserialize(message);
                await dispatcher.DispatchAsync(domainEvent, ct);
                message.MarkProcessed(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                // Per-message failure: record (increments Attempts) for backoff/retry/cap.
                _logger.LogWarning(
                    ex, "Outbox message {OutboxId} (event {EventId}) dispatch failed (attempt {Attempt}).",
                    message.Id, message.EventId, message.Attempts + 1);
                message.MarkFailed(Truncate(ex.Message));
            }
        }

        await db.SaveChangesAsync(ct);
        return messages.Count;
    }

    // Resolve the CLR type from the stored assembly-qualified name and deserialize the payload.
    private static IDomainEvent Deserialize(OutboxMessage message)
    {
        var type = Type.GetType(message.Type, throwOnError: false)
                   ?? throw new InvalidOperationException($"Unknown outbox event type '{message.Type}'.");

        // Use default serializer options to match AtriaDbContext.WriteOutboxMessages (no Web casing).
        if (JsonSerializer.Deserialize(message.Payload, type) is not IDomainEvent domainEvent)
            throw new InvalidOperationException($"Outbox payload did not deserialize to an IDomainEvent for '{message.Type}'.");

        return domainEvent;
    }

    // Exponential-ish backoff capped at MaxDelay.
    private static TimeSpan NextDelay(TimeSpan current)
    {
        var next = TimeSpan.FromTicks(current.Ticks * 2);
        return next > MaxDelay ? MaxDelay : next;
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested during delay — let the loop exit.
        }
    }

    // Keep stored error short and bounded.
    private static string Truncate(string value)
        => value.Length <= 1000 ? value : value[..1000];
}
