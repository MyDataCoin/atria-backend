using Atria.Domain.Regulatory;
using FluentAssertions;

namespace Atria.Domain.Tests.Regulatory;

/// <summary>
/// Every deadline in the draft Decree is stated in working days. Counting calendar days would file
/// some notifications late, which is the one mistake this arithmetic exists to prevent.
/// </summary>
public sealed class ReportDeadlineTests
{
    // 2026-08-03 is a Monday.
    private static readonly DateTime Monday = new(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Friday = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Weekends_do_not_count_towards_a_deadline()
    {
        // Five working days from a Friday lands on the Friday of the following week, not on Wednesday.
        ReportDeadline.AddWorkingDays(Friday, 5).Date.Should().Be(new DateTime(2026, 8, 14));
    }

    [Fact]
    public void Counting_starts_the_day_after_the_period_ends()
    {
        ReportDeadline.AddWorkingDays(Monday, 1).Date.Should().Be(new DateTime(2026, 8, 4));
    }

    [Fact]
    public void A_deadline_landing_on_a_weekend_is_pushed_to_the_next_working_day()
    {
        // Monday + 5 working days = Monday the 10th; the weekend in between is skipped.
        var due = ReportDeadline.AddWorkingDays(Monday, 5);

        due.Date.Should().Be(new DateTime(2026, 8, 10));
        ReportDeadline.IsWorkingDay(due).Should().BeTrue();
    }

    [Fact]
    public void Zero_working_days_is_the_same_instant()
    {
        ReportDeadline.AddWorkingDays(Monday, 0).Should().Be(Monday);
    }

    [Fact]
    public void A_negative_span_is_refused()
    {
        var act = () => ReportDeadline.AddWorkingDays(Monday, -1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(RegulatoryReportKind.FundsReceivedAndIssued, 5)]   // §24
    [InlineData(RegulatoryReportKind.MonthlyPlacement, 10)]        // §49
    [InlineData(RegulatoryReportKind.PlacementResults, 5)]         // §§50, 52
    [InlineData(RegulatoryReportKind.AmlQuarterly, 15)]            // §80
    public void Each_notification_keeps_the_span_the_decree_gives_it(RegulatoryReportKind kind, int expected)
    {
        ReportDeadline.WorkingDaysFor(kind).Should().Be(expected);
    }

    [Fact]
    public void Saturday_and_Sunday_are_not_working_days()
    {
        ReportDeadline.IsWorkingDay(new DateTime(2026, 8, 8)).Should().BeFalse();  // Saturday
        ReportDeadline.IsWorkingDay(new DateTime(2026, 8, 9)).Should().BeFalse();  // Sunday
        ReportDeadline.IsWorkingDay(new DateTime(2026, 8, 10)).Should().BeTrue();  // Monday
    }
}
