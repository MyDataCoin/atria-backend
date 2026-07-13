using Atria.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Deals;

/// <summary>
/// Periodically rejects referral deals whose links have expired unused (Pending -> Rejected). Runs
/// on a fixed interval; a fresh DI scope is created per sweep so scoped services (DbContext,
/// repository) resolve correctly. Failures are logged and retried on the next tick.
/// </summary>
public sealed class DealExpiryBackgroundService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DealExpiryBackgroundService> _logger;

    public DealExpiryBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<DealExpiryBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

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
                _logger.LogError(ex, "Deal expiry sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var deals = scope.ServiceProvider.GetRequiredService<IDealRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var expired = await deals.GetExpiredPendingAsync(clock.UtcNow, ct);
        if (expired.Count == 0)
            return;

        foreach (var deal in expired)
        {
            deal.Reject();
            deals.Update(deal);
        }

        await unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Rejected {Count} expired referral deal(s).", expired.Count);
    }
}
