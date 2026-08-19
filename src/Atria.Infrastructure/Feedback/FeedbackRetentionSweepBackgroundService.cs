using Atria.Application.Abstractions;
using Atria.Domain.Feedback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Feedback;

/// <summary>
/// Deletes feedback requests older than <see cref="FeedbackRequest.RetentionDays"/> days.
/// </summary>
/// <remarks>
/// The consent text on the public site tells people their name and contact are collected only to
/// answer them and are not kept long. That sentence is only true if something actually deletes the
/// rows: an unbounded table of names and phone numbers is exactly the pile of personal data the
/// promise says we do not keep. Answered or not, a request that old is no longer worth answering.
/// </remarks>
public sealed class FeedbackRetentionSweepBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(12);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FeedbackRetentionSweepBackgroundService> _logger;

    public FeedbackRetentionSweepBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<FeedbackRetentionSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IFeedbackRequestRepository>();
                var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

                var cutoff = clock.UtcNow.AddDays(-FeedbackRequest.RetentionDays);
                var deleted = await repo.DeleteOlderThanAsync(cutoff, stoppingToken);
                if (deleted > 0)
                    _logger.LogInformation("Swept {Count} feedback request(s) past retention.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Housekeeping must never take the host down: log and try again next tick.
                _logger.LogError(ex, "Feedback retention sweep failed; retrying next interval.");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
