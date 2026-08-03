using Atria.Application.Abstractions;
using Atria.Application.Regulatory.Commands;
using Atria.Application.Regulatory.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atria.Infrastructure.Regulatory;

/// <summary>
/// Keeps the register of filing obligations current: records the notifications for every period that
/// has closed, and logs a warning as each deadline approaches or passes.
///
/// The reminder is a log line and the <c>outstanding</c> endpoint rather than an email: the operator
/// works from the admin panel, and a deadline that only exists in someone's inbox is a deadline that
/// gets missed. The sweep is idempotent — an obligation already recorded is left alone — so a restart
/// or a double tick changes nothing.
/// </summary>
public sealed class RegulatoryReportSweepBackgroundService : BackgroundService
{
    /// <summary>Deadlines move in days, not minutes; once every six hours is ample.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    /// <summary>Working days out at which a deadline starts being called out.</summary>
    private const int WarnWithinWorkingDays = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RegulatoryReportSweepBackgroundService> _logger;

    public RegulatoryReportSweepBackgroundService(
        IServiceScopeFactory scopeFactory, ILogger<RegulatoryReportSweepBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

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
                _logger.LogError(ex, "Regulatory report sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var raised = await sender.Send(new RaiseDueRegulatoryReportsCommand(clock.UtcNow), ct);
        if (raised.IsSuccess && raised.Value > 0)
            _logger.LogInformation("Recorded {Count} new regulatory filing obligations.", raised.Value);

        var outstanding = await sender.Send(new GetOutstandingReportsQuery(), ct);
        if (outstanding.IsFailure)
            return;

        foreach (var report in outstanding.Value)
        {
            if (report.IsOverdue)
            {
                _logger.LogError(
                    "Regulatory notification {Kind} for period ending {PeriodEnd:yyyy-MM-dd} is OVERDUE "
                    + "(was due {Due:yyyy-MM-dd}, report {ReportId}).",
                    report.Kind, report.PeriodEndUtc, report.DueAtUtc, report.Id);
            }
            else if (report.WorkingDaysLeft <= WarnWithinWorkingDays)
            {
                _logger.LogWarning(
                    "Regulatory notification {Kind} for period ending {PeriodEnd:yyyy-MM-dd} is due "
                    + "{Due:yyyy-MM-dd} — {DaysLeft} working day(s) left (report {ReportId}).",
                    report.Kind, report.PeriodEndUtc, report.DueAtUtc, report.WorkingDaysLeft, report.Id);
            }
        }
    }
}
