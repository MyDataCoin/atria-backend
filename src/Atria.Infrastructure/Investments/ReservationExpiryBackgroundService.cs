using Atria.Application.Abstractions;
using Atria.Application.Investments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Investments;

/// <summary>
/// Periodically reclaims offering applications whose reservation window has lapsed without operator
/// approval (Reserved -> Expired), returning their held tokens to the property's pool. Without this,
/// an abandoned application would hold its tokens forever and AvailableTokens would drain to zero
/// without a single sale.
///
/// Runs on a configured interval; a fresh DI scope is created per sweep so scoped services (DbContext,
/// repositories) resolve correctly. Each lapsed reservation is expired and its tokens released in one
/// unit of work. Work is capped per sweep (batch) so a large backlog drains across ticks rather than
/// blocking a single transaction. The pass is idempotent: an already-expired application no longer
/// matches the Reserved filter, so a re-run changes nothing. Failures are logged and retried next tick.
/// </summary>
public sealed class ReservationExpiryBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InvestmentReservationOptions _options;
    private readonly ILogger<ReservationExpiryBackgroundService> _logger;

    public ReservationExpiryBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<InvestmentReservationOptions> options,
        ILogger<ReservationExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.SweepInterval);

        // Sweep once at startup, then on each tick.
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
                _logger.LogError(ex, "Reservation expiry sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var investments = scope.ServiceProvider.GetRequiredService<IInvestmentRepository>();
        var properties = scope.ServiceProvider.GetRequiredService<IPropertyRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var expired = await investments.GetExpiredReservationsAsync(
            clock.UtcNow, _options.SweepBatchSize, ct);
        if (expired.Count == 0)
            return;

        var reclaimed = 0;
        foreach (var investment in expired)
        {
            var property = await properties.GetByIdAsync(investment.PropertyId, ct);
            if (property is null)
            {
                // Property gone (shouldn't happen for a live reservation): skip, don't block the batch.
                _logger.LogWarning(
                    "Skipping expiry of investment {InvestmentId}: property {PropertyId} not found.",
                    investment.Id, investment.PropertyId);
                continue;
            }

            investment.Expire();
            property.ReleaseTokens(investment.TokenCount);

            investments.Update(investment);
            properties.Update(property);
            reclaimed++;
        }

        if (reclaimed == 0)
            return;

        await unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Expired {Count} lapsed reservation(s), returning tokens to the pool.", reclaimed);
    }
}
