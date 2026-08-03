namespace Atria.Domain.Regulatory;

/// <summary>
/// Turns "5 working days after the event" into an instant. Every deadline in the draft Decree is
/// stated in working days, so counting calendar days would file some notifications late.
/// </summary>
/// <remarks>
/// Saturday and Sunday are skipped. Public holidays are NOT — the platform has no holiday calendar,
/// and inventing one would silently push deadlines later than the law allows. Erring early is safe;
/// erring late is not. When a holiday calendar is available, feed it in here and nothing else changes.
/// </remarks>
public static class ReportDeadline
{
    /// <summary>Working days each kind of notification allows after the period it covers ends.</summary>
    public static int WorkingDaysFor(RegulatoryReportKind kind) => kind switch
    {
        RegulatoryReportKind.FundsReceivedAndIssued => 5,   // §24
        RegulatoryReportKind.MonthlyPlacement => 10,        // §49
        RegulatoryReportKind.PlacementResults => 5,         // §§50, 52
        RegulatoryReportKind.CollateralInterim => 10,       // §15 — monthly, filed like the monthly report
        RegulatoryReportKind.AmlQuarterly => 15,            // §80
        _ => 5
    };

    /// <summary>
    /// The instant a notification covering a period ending at <paramref name="periodEndUtc"/> is due.
    /// The end of the period does not itself consume a working day: counting starts the next day.
    /// </summary>
    public static DateTime For(RegulatoryReportKind kind, DateTime periodEndUtc)
        => AddWorkingDays(periodEndUtc, WorkingDaysFor(kind));

    /// <summary>Advances <paramref name="from"/> by <paramref name="workingDays"/>, skipping weekends.</summary>
    public static DateTime AddWorkingDays(DateTime from, int workingDays)
    {
        if (workingDays < 0)
            throw new ArgumentOutOfRangeException(nameof(workingDays), "Working days cannot be negative.");

        var result = from;
        var remaining = workingDays;

        while (remaining > 0)
        {
            result = result.AddDays(1);
            if (IsWorkingDay(result))
                remaining--;
        }

        return result;
    }

    public static bool IsWorkingDay(DateTime date)
        => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
