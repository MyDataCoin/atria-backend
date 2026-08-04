using Atria.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Identity;

/// <summary>
/// Deletes refresh tokens that expired long enough ago to be of no use to anyone.
/// </summary>
/// <remarks>
/// Rotation writes a row per refresh, so an active user produces a few hundred a year and the table
/// only ever grows. Nothing reads an expired row — lookups are by hash and every path checks the
/// expiry — so keeping them costs index size and backup time for no benefit. The grace period leaves
/// recently expired rows in place a while, so an investigation into a suspected token leak still has
/// something to look at.
/// </remarks>
public sealed class ExpiredRefreshTokenSweepBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan Grace = TimeSpan.FromDays(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpiredRefreshTokenSweepBackgroundService> _logger;

    public ExpiredRefreshTokenSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ExpiredRefreshTokenSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
                var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

                var deleted = await store.DeleteExpiredAsync(clock.UtcNow - Grace, stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Swept {Count} expired refresh token(s).", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping must never take the host down: log and try again next tick.
                _logger.LogError(ex, "Expired refresh token sweep failed; retrying next interval.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
