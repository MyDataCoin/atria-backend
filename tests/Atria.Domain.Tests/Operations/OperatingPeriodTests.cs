using Atria.Domain.Common;
using Atria.Domain.Operations;
using FluentAssertions;

namespace Atria.Domain.Tests.Operations;

/// <summary>
/// A distribution used to start from a number somebody typed. This is the record that answers "where
/// did that number come from": what the issue earned, what it spent, and who signed the figures off.
/// <para>
/// Two rules carry the weight. The person who reports the figures cannot be the person who confirms
/// them — the management company asked for exactly that split. And a period pays out once, for no
/// more than it earned: without either rule the same income could be distributed twice, or an amount
/// the object never made could go out against it.
/// </para>
/// </summary>
public sealed class OperatingPeriodTests
{
    private static readonly Guid Issue = Guid.NewGuid();
    private static readonly Guid Accountant = Guid.NewGuid();
    private static readonly Guid Approver = Guid.NewGuid();
    private static readonly DateTime Start = new(2027, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = new(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2028, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static OperatingPeriod NewPeriod(decimal revenue = 500_000m, decimal expenses = 120_000m)
        => OperatingPeriod.Report(Issue, Start, End, revenue, expenses, "KGS", Accountant, "IV квартал");

    private static OperatingPeriod NewConfirmedPeriod(
        decimal revenue = 500_000m, decimal expenses = 120_000m)
    {
        var period = NewPeriod(revenue, expenses);
        period.Confirm(Approver, Now);
        return period;
    }

    [Fact]
    public void Report_recordsTheFiguresAsADraft()
    {
        var period = NewPeriod();

        period.Status.Should().Be(OperatingPeriodStatus.Draft);
        period.NetIncome.Should().Be(380_000m);
        period.HasDistributableIncome.Should().BeTrue();
        period.ReportedByUserId.Should().Be(Accountant);
        period.ConfirmedByUserId.Should().BeNull();
    }

    [Fact]
    public void Report_acceptsALossMakingPeriod()
    {
        // A loss is a fact to record, not an error. It simply has nothing to distribute.
        var period = NewPeriod(revenue: 100_000m, expenses: 180_000m);

        period.NetIncome.Should().Be(-80_000m);
        period.HasDistributableIncome.Should().BeFalse();
    }

    [Fact]
    public void Report_refusesAPeriodThatEndsBeforeItStarts()
    {
        var act = () => OperatingPeriod.Report(
            Issue, End, Start, 1m, 0m, "KGS", Accountant, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Report_refusesFiguresFinerThanMinorUnits()
    {
        // The figures become the ceiling on what may be paid out; a third decimal would make the
        // period and the run disagree about that ceiling by a fraction nobody could account for.
        var act = () => OperatingPeriod.Report(
            Issue, Start, End, 500_000.001m, 0m, "KGS", Accountant, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Confirm_freezesTheFiguresAndNamesTheConfirmer()
    {
        var period = NewPeriod();

        period.Confirm(Approver, Now);

        period.Status.Should().Be(OperatingPeriodStatus.Confirmed);
        period.ConfirmedByUserId.Should().Be(Approver);
        period.ConfirmedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Confirm_refusesTheSamePersonWhoReportedTheFigures()
    {
        // The point of confirming is that a second pair of eyes saw the numbers.
        var period = NewPeriod();

        var act = () => period.Confirm(Accountant, Now);

        act.Should().Throw<DomainException>();
        period.Status.Should().Be(OperatingPeriodStatus.Draft);
    }

    [Fact]
    public void Revise_correctsADraft()
    {
        var period = NewPeriod();

        period.Revise(600_000m, 100_000m, "уточнено");

        period.NetIncome.Should().Be(500_000m);
    }

    [Fact]
    public void Revise_refusesOnceTheFiguresAreConfirmed()
    {
        // A distribution may already have been sized against them.
        var period = NewConfirmedPeriod();

        var act = () => period.Revise(900_000m, 0m, null);

        act.Should().Throw<DomainException>();
        period.NetIncome.Should().Be(380_000m);
    }

    [Fact]
    public void ReplaceLines_keepsTheBreakdownInOrder()
    {
        var period = NewPeriod();

        period.ReplaceLines(
        [
            (OperatingLineKind.Revenue, "Аренда 1 этаж", 420_000m),
            (OperatingLineKind.Revenue, "Паркинг", 80_000m),
            (OperatingLineKind.Expense, "Коммунальные", 120_000m)
        ]);

        period.Lines.Should().HaveCount(3);
        period.Lines.Select(l => l.Label).Should()
            .ContainInOrder("Аренда 1 этаж", "Паркинг", "Коммунальные");
    }

    [Fact]
    public void ReplaceLines_doesNotForceTheBreakdownToMatchTheTotals()
    {
        // A partial breakdown is more useful than none; a caller that wants to flag a discrepancy
        // compares the sums itself.
        var period = NewPeriod();

        var act = () => period.ReplaceLines([(OperatingLineKind.Revenue, "Часть аренды", 1_000m)]);

        act.Should().NotThrow();
        period.GrossRevenue.Should().Be(500_000m);
    }

    [Fact]
    public void ReplaceLines_refusesANonPositiveAmount()
    {
        // The kind carries the sign; a negative expense would be a second, contradictory way of
        // saying "revenue".
        var period = NewPeriod();

        var act = () => period.ReplaceLines([(OperatingLineKind.Expense, "Возврат", -500m)]);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkDistributed_recordsTheRunAndClosesThePeriod()
    {
        var period = NewConfirmedPeriod();
        var run = Guid.NewGuid();

        period.MarkDistributed(run, 380_000m);

        period.Status.Should().Be(OperatingPeriodStatus.Distributed);
        period.PayoutRunId.Should().Be(run);
    }

    [Fact]
    public void MarkDistributed_refusesAnUnconfirmedPeriod()
    {
        var period = NewPeriod();

        var act = () => period.MarkDistributed(Guid.NewGuid(), 100_000m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void MarkDistributed_refusesMoreThanThePeriodEarned()
    {
        var period = NewConfirmedPeriod();

        var act = () => period.MarkDistributed(Guid.NewGuid(), 380_000.01m);

        act.Should().Throw<DomainException>();
        period.Status.Should().Be(OperatingPeriodStatus.Confirmed);
    }

    [Fact]
    public void MarkDistributed_refusesASecondDistributionFromTheSameIncome()
    {
        // The distribution equivalent of dividing one register twice: the money would go out again
        // against income that only existed once.
        var period = NewConfirmedPeriod();
        period.MarkDistributed(Guid.NewGuid(), 100_000m);

        var act = () => period.MarkDistributed(Guid.NewGuid(), 100_000m);

        act.Should().Throw<DomainException>();
    }
}
