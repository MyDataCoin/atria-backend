using Atria.Domain.Common;
using Atria.Domain.Regulatory;
using FluentAssertions;

namespace Atria.Domain.Tests.Regulatory;

/// <summary>
/// The obligation is recorded before anything is produced — a deadline nobody has written down is a
/// deadline nobody is reminded of — and what was filed stays filed.
/// </summary>
public sealed class RegulatoryReportTests
{
    private static readonly Guid PropertyId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime PeriodStart = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);

    private static RegulatoryReport Raised(RegulatoryReportKind kind = RegulatoryReportKind.MonthlyPlacement)
        => RegulatoryReport.Raise(kind, PropertyId, PeriodStart, PeriodEnd);

    [Fact]
    public void An_obligation_is_recorded_due_with_its_deadline_derived_from_the_period()
    {
        var report = Raised();

        report.Status.Should().Be(RegulatoryReportStatus.Due);
        report.Content.Should().BeNull();
        report.DueAtUtc.Should().Be(ReportDeadline.For(RegulatoryReportKind.MonthlyPlacement, PeriodEnd));
    }

    [Fact]
    public void A_period_cannot_end_before_it_starts()
    {
        var act = () => RegulatoryReport.Raise(
            RegulatoryReportKind.AmlQuarterly, null, PeriodEnd, PeriodStart);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Attaching_content_marks_it_generated_and_records_the_snapshot_it_came_from()
    {
        var report = Raised(RegulatoryReportKind.PlacementResults);
        var snapshotId = Guid.NewGuid();
        var at = new DateTime(2026, 8, 3, 9, 0, 0, DateTimeKind.Utc);

        report.AttachContent("{\"placed\":100}", snapshotId, at);

        report.Status.Should().Be(RegulatoryReportStatus.Generated);
        report.Content.Should().Be("{\"placed\":100}");
        report.HolderSnapshotId.Should().Be(snapshotId);
        report.GeneratedAtUtc.Should().Be(at);
    }

    /// <summary>Figures move up to the moment the notification is handed over.</summary>
    [Fact]
    public void Content_can_be_regenerated_while_the_report_is_not_filed()
    {
        var report = Raised();
        report.AttachContent("{\"placed\":100}", null, DateTime.UtcNow);

        report.AttachContent("{\"placed\":120}", null, DateTime.UtcNow);

        report.Content.Should().Be("{\"placed\":120}");
    }

    [Fact]
    public void A_filed_report_can_never_be_regenerated()
    {
        var report = Raised();
        report.AttachContent("{\"placed\":100}", null, DateTime.UtcNow);
        report.MarkFiled("ISX-2026-07-001", DateTime.UtcNow);

        var act = () => report.AttachContent("{\"placed\":120}", null, DateTime.UtcNow);

        act.Should().Throw<DomainException>().WithMessage("*filed report cannot be regenerated*");
        report.Content.Should().Be("{\"placed\":100}");
    }

    [Fact]
    public void Only_a_generated_report_can_be_filed()
    {
        var report = Raised();

        var act = () => report.MarkFiled("ISX-2026-07-001", DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Filing_requires_the_regulators_reference()
    {
        var report = Raised();
        report.AttachContent("{}", null, DateTime.UtcNow);

        var act = () => report.MarkFiled("  ", DateTime.UtcNow);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void An_unfiled_report_is_overdue_once_its_deadline_passes()
    {
        var report = Raised();

        report.IsOverdueAt(report.DueAtUtc.AddMinutes(-1)).Should().BeFalse();
        report.IsOverdueAt(report.DueAtUtc.AddMinutes(1)).Should().BeTrue();
    }

    [Fact]
    public void A_filed_report_is_never_overdue_afterwards()
    {
        var report = Raised();
        report.AttachContent("{}", null, DateTime.UtcNow);
        report.MarkFiled("ISX-2026-07-001", DateTime.UtcNow);

        report.IsOverdueAt(report.DueAtUtc.AddYears(1)).Should().BeFalse();
    }

    [Fact]
    public void Working_days_left_counts_down_and_stops_at_zero()
    {
        var report = Raised();

        report.WorkingDaysLeft(report.DueAtUtc).Should().Be(0);
        report.WorkingDaysLeft(report.DueAtUtc.AddDays(5)).Should().Be(0);
        report.WorkingDaysLeft(PeriodEnd).Should().BeGreaterThan(0);
    }
}
