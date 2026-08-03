using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;
using Atria.Domain.Regulatory;

namespace Atria.Application.Regulatory.Commands;

/// <summary>
/// Records the periodic filing obligations for every period that has closed and is not yet on the
/// books: the monthly placement and interim collateral notifications per open issue, and the quarterly
/// AML notification for the platform.
/// </summary>
/// <param name="AsOfUtc">The instant to reckon closed periods from.</param>
public sealed record RaiseDueRegulatoryReportsCommand(DateTime AsOfUtc) : IRequest<Result<int>>;

/// <summary>
/// Runs periodically and on demand. Idempotent: an obligation already recorded for a period is left
/// alone, so re-running raises nothing new. Only periods that have actually closed are recorded — a
/// deadline for a month still in progress would be meaningless.
/// </summary>
public sealed class RaiseDueRegulatoryReportsCommandHandler
    : IRequestHandler<RaiseDueRegulatoryReportsCommand, Result<int>>
{
    /// <summary>How far back to look for closed periods that were never recorded.</summary>
    private const int MonthsToBackfill = 12;

    private readonly IRegulatoryReportRepository _reports;
    private readonly IPropertyRepository _properties;
    private readonly IUnitOfWork _uow;

    public RaiseDueRegulatoryReportsCommandHandler(
        IRegulatoryReportRepository reports, IPropertyRepository properties, IUnitOfWork uow)
    {
        _reports = reports;
        _properties = properties;
        _uow = uow;
    }

    public async Task<Result<int>> Handle(RaiseDueRegulatoryReportsCommand request, CancellationToken ct)
    {
        var raised = 0;

        // Issues that have been published: before that there is nothing placed to report on.
        var issues = (await _properties.GetAllAsync(ct))
            .Where(p => p.Status is PropertyStatus.Open or PropertyStatus.Completed)
            .ToList();

        foreach (var (start, end) in ClosedMonths(request.AsOfUtc))
        {
            foreach (var issue in issues)
            {
                // Only for issues that existed during the period.
                if (issue.CreatedAtUtc > end)
                    continue;

                raised += await EnsureAsync(RegulatoryReportKind.MonthlyPlacement, issue.Id, start, end, ct);
                raised += await EnsureAsync(RegulatoryReportKind.CollateralInterim, issue.Id, start, end, ct);
            }
        }

        foreach (var (start, end) in ClosedQuarters(request.AsOfUtc))
            raised += await EnsureAsync(RegulatoryReportKind.AmlQuarterly, null, start, end, ct);

        if (raised > 0)
            await _uow.SaveChangesAsync(ct);

        return Result.Success(raised);
    }

    private async Task<int> EnsureAsync(
        RegulatoryReportKind kind, Guid? propertyId, DateTime start, DateTime end, CancellationToken ct)
    {
        if (await _reports.FindAsync(kind, propertyId, end, ct) is not null)
            return 0;

        await _reports.AddAsync(RegulatoryReport.Raise(kind, propertyId, start, end), ct);
        return 1;
    }

    /// <summary>Months that have fully ended, most recent first, within the backfill window.</summary>
    private static IEnumerable<(DateTime Start, DateTime End)> ClosedMonths(DateTime asOfUtc)
    {
        var firstOfThisMonth = new DateTime(asOfUtc.Year, asOfUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= MonthsToBackfill; i++)
        {
            var start = firstOfThisMonth.AddMonths(-i);
            yield return (start, start.AddMonths(1).AddTicks(-1));
        }
    }

    /// <summary>Quarters that have fully ended, most recent first, within the backfill window.</summary>
    private static IEnumerable<(DateTime Start, DateTime End)> ClosedQuarters(DateTime asOfUtc)
    {
        var currentQuarterStartMonth = ((asOfUtc.Month - 1) / 3) * 3 + 1;
        var thisQuarterStart = new DateTime(asOfUtc.Year, currentQuarterStartMonth, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= MonthsToBackfill / 3; i++)
        {
            var start = thisQuarterStart.AddMonths(-3 * i);
            yield return (start, start.AddMonths(3).AddTicks(-1));
        }
    }
}
