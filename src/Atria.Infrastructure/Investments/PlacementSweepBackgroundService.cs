using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Properties;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Investments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Investments;

/// <summary>
/// Runs the placement calendar: opens an offering when its scheduled start arrives, and closes it
/// when its end does. Without this the dates on an issue would be documentation — someone would still
/// have to press publish at midnight.
/// </summary>
/// <remarks>
/// <para>
/// What it deliberately does NOT do is decide the fate of an offering that closed short of its
/// target. The management company chose "возврат и продление", and both of those are decisions:
/// extending changes the terms investors bought under, and unwinding returns everyone's money. The
/// sweep closes the placement and says loudly in the journal that the target was missed; a human
/// then extends it or unwinds it. Automating that choice would mean the platform silently picking
/// one for them.
/// </para>
/// <para>
/// Idempotent by construction: a closed issue no longer matches the "open and due to close" filter,
/// so a re-run changes nothing. Failures are logged and retried on the next tick.
/// </para>
/// </remarks>
public sealed class PlacementSweepBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlacementSweepOptions _options;
    private readonly ILogger<PlacementSweepBackgroundService> _logger;

    public PlacementSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PlacementSweepOptions> options,
        ILogger<PlacementSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SweepInterval);

        do
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Placement sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyRepository>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var now = clock.UtcNow;
        var due = await properties.GetDuePlacementsAsync(now, _options.SweepBatchSize, ct);
        if (due.Count == 0)
            return;

        var moved = 0;

        foreach (var property in due)
        {
            try
            {
                if (property.Status == PropertyStatus.Open)
                {
                    property.Complete();

                    // The journal is where a missed target is raised, because nothing downstream is
                    // going to act on it automatically.
                    var missed = !property.IsTargetMet;
                    await audit.WriteAsync(
                        AuditEntities.Property, property.Id, AuditEvents.PlacementClosed,
                        $"Размещение объекта «{property.Name}» закрыто по сроку. "
                        + $"Собрано {property.RaisedAmount:0.##} {property.Currency}"
                        + (property.TargetAmount is { } target ? $" из {target:0.##}" : string.Empty)
                        + (missed ? ". ЦЕЛЬ НЕ ДОСТИГНУТА — требуется решение: продление или возврат" : string.Empty),
                        missed ? AuditSeverity.Alert : AuditSeverity.Success, ct);
                }
                else
                {
                    property.Publish();

                    await audit.WriteAsync(
                        AuditEntities.Property, property.Id, AuditEvents.PlacementOpened,
                        $"Размещение объекта «{property.Name}» открыто по расписанию", AuditSeverity.Success, ct);
                }

                properties.Update(property);
                moved++;
            }
            catch (DomainException ex)
            {
                // A status the calendar cannot move from (invalidated, already completed). Skipping
                // keeps one stuck issue from blocking every other one due in the same tick.
                _logger.LogWarning(
                    ex, "Skipping scheduled placement move for property {PropertyId}.", property.Id);
            }
        }

        if (moved == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Placement sweep moved {Count} offering(s) on schedule.", moved);
    }
}
