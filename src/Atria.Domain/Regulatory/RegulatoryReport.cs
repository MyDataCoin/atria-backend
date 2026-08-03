using Atria.Domain.Common;

namespace Atria.Domain.Regulatory;

/// <summary>
/// One notification the issuer owes the regulator: which one, for what period, by when, and — once
/// produced — the content generated from system data.
///
/// The obligation is recorded the moment it arises, before anything is produced. That is the whole
/// point: a deadline nobody has written down is a deadline nobody is reminded of.
/// </summary>
public sealed class RegulatoryReport : AggregateRoot
{
    /// <summary>Which notification this is.</summary>
    public RegulatoryReportKind Kind { get; private set; }

    /// <summary>
    /// The issue it concerns. Null for platform-wide notifications — the AML report covers the
    /// operator, not a single issue.
    /// </summary>
    public Guid? PropertyId { get; private set; }

    /// <summary>Start of the period covered.</summary>
    public DateTime PeriodStartUtc { get; private set; }

    /// <summary>End of the period covered. The deadline counts from here.</summary>
    public DateTime PeriodEndUtc { get; private set; }

    /// <summary>When it must be filed, in working days from <see cref="PeriodEndUtc"/>.</summary>
    public DateTime DueAtUtc { get; private set; }

    public RegulatoryReportStatus Status { get; private set; }

    /// <summary>The generated document, as JSON. Null until it is produced.</summary>
    public string? Content { get; private set; }

    public DateTime? GeneratedAtUtc { get; private set; }

    /// <summary>
    /// The holder snapshot the content was drawn from, where one applies. Recorded so the figures
    /// filed can be traced back to the frozen register they came from rather than to live data that
    /// has since moved.
    /// </summary>
    public Guid? HolderSnapshotId { get; private set; }

    public DateTime? FiledAtUtc { get; private set; }

    /// <summary>The regulator's acknowledgement reference, once filed.</summary>
    public string? FilingReference { get; private set; }

    private RegulatoryReport() { }

    /// <summary>Records the obligation, with its deadline derived from the period it covers.</summary>
    public static RegulatoryReport Raise(
        RegulatoryReportKind kind, Guid? propertyId, DateTime periodStartUtc, DateTime periodEndUtc)
    {
        if (periodEndUtc < periodStartUtc)
            throw new DomainException("A reporting period cannot end before it starts.");

        return new RegulatoryReport
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            PropertyId = propertyId,
            PeriodStartUtc = periodStartUtc,
            PeriodEndUtc = periodEndUtc,
            DueAtUtc = ReportDeadline.For(kind, periodEndUtc),
            Status = RegulatoryReportStatus.Due
        };
    }

    /// <summary>
    /// Attaches the content generated from system data. Regenerating is allowed while the report has
    /// not been filed — a figure can still change up to the moment it is handed over — but never
    /// afterwards: what was filed is what was filed.
    /// </summary>
    public void AttachContent(string content, Guid? holderSnapshotId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new DomainException("Report content is required.");
        if (Status == RegulatoryReportStatus.Filed)
            throw new DomainException("A filed report cannot be regenerated.");

        Content = content;
        HolderSnapshotId = holderSnapshotId;
        GeneratedAtUtc = nowUtc;
        Status = RegulatoryReportStatus.Generated;
    }

    /// <summary>Marks the notification as filed, with the regulator's reference.</summary>
    public void MarkFiled(string filingReference, DateTime nowUtc)
    {
        if (Status != RegulatoryReportStatus.Generated)
            throw new DomainException("Only a generated report can be filed.");
        if (string.IsNullOrWhiteSpace(filingReference))
            throw new DomainException("A filing reference is required.");

        FilingReference = filingReference;
        FiledAtUtc = nowUtc;
        Status = RegulatoryReportStatus.Filed;
    }

    /// <summary>Past its deadline and still not filed.</summary>
    public bool IsOverdueAt(DateTime nowUtc)
        => Status != RegulatoryReportStatus.Filed && nowUtc > DueAtUtc;

    /// <summary>Working days left before the deadline; zero once it has passed.</summary>
    public int WorkingDaysLeft(DateTime nowUtc)
    {
        if (nowUtc >= DueAtUtc)
            return 0;

        var days = 0;
        for (var d = nowUtc.Date.AddDays(1); d <= DueAtUtc.Date; d = d.AddDays(1))
            if (ReportDeadline.IsWorkingDay(d))
                days++;

        return days;
    }
}
