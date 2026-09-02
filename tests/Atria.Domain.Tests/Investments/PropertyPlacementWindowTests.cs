using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers the placement window: when an offering is scheduled to open and close, what it is trying
/// to raise, and what pushing the closing date back does.
/// <para>
/// The window is a schedule, not a switch — the sweep reads these dates and moves the status. What
/// is pinned here is the arithmetic the sweep depends on, and the one shape the window must never
/// take: closing before it opens, which would make an issue simultaneously due to open and due to
/// close and leave the outcome to whichever check ran first.
/// </para>
/// </summary>
public sealed class PropertyPlacementWindowTests
{
    private static readonly DateTime Opens = new(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Closes = new(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    // 1 000 shares at 1 000 KGS: the whole issue is 1 000 000.
    private static Property NewIssue() => Property.Create(
        "ЖК на Токомбаева", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void SchedulePlacement_StoresTheWindowAndTheTarget()
    {
        var issue = NewIssue();

        issue.SchedulePlacement(Opens, Closes, 750_000m);

        issue.PlacementOpensAtUtc.Should().Be(Opens);
        issue.PlacementClosesAtUtc.Should().Be(Closes);
        issue.TargetAmount.Should().Be(750_000m);
    }

    [Fact]
    public void SchedulePlacement_RefusesAWindowThatClosesBeforeItOpens()
    {
        var issue = NewIssue();

        var act = () => issue.SchedulePlacement(Closes, Opens);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SchedulePlacement_RefusesToInvertTheWindowOneFieldAtATime()
    {
        // The check is against what is already stored, not just against the arguments — otherwise a
        // PATCH of a single date could assemble the window the call above refuses.
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        var act = () => issue.SchedulePlacement(closesAtUtc: Opens.AddDays(-1));

        act.Should().Throw<DomainException>();
        issue.PlacementClosesAtUtc.Should().Be(Closes);
    }

    [Fact]
    public void SchedulePlacement_RejectsANonPositiveTarget()
    {
        var issue = NewIssue();

        var act = () => issue.SchedulePlacement(targetAmount: 0m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RaisedAmount_CountsWhatHasLeftTheSupplyAtTheIssuePrice()
    {
        var issue = NewIssue();

        issue.ReserveTokens(300L);

        issue.RaisedAmount.Should().Be(300_000m);
    }

    [Fact]
    public void IsTargetMet_IsFalseWhileThePlacementIsShort()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes, 750_000m);

        issue.ReserveTokens(300L);

        issue.IsTargetMet.Should().BeFalse();
    }

    [Fact]
    public void IsTargetMet_IsTrueOnceTheTargetIsReached()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes, 750_000m);

        issue.ReserveTokens(750L);

        issue.IsTargetMet.Should().BeTrue();
    }

    [Fact]
    public void IsTargetMet_IsTrueWhenNoTargetWasEverSet()
    {
        // An issue that never set a target cannot fall short of one, and the sweep must not raise an
        // alert for every placement that simply sells what it sells.
        var issue = NewIssue();

        issue.IsTargetMet.Should().BeTrue();
    }

    [Fact]
    public void IsPlacementOpeningDue_TurnsTrueOnlyAtTheScheduledTime()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        issue.IsPlacementOpeningDue(Opens.AddSeconds(-1)).Should().BeFalse();
        issue.IsPlacementOpeningDue(Opens).Should().BeTrue();
    }

    [Fact]
    public void IsPlacementOpeningDue_IsFalseOnceTheClosingTimeHasPassedToo()
    {
        // An issue whose whole window elapsed before anyone looked must not be opened by the sweep
        // only to be closed on the same pass.
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        issue.IsPlacementOpeningDue(Closes.AddDays(1)).Should().BeFalse();
        issue.IsPlacementClosingDue(Closes.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void IsPlacementClosingDue_IsFalseWhenNoClosingDateWasSet()
    {
        var issue = NewIssue();

        issue.IsPlacementClosingDue(DateTime.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void ExtendPlacement_MovesTheDateAndCountsTheExtension()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        issue.ExtendPlacement(Closes.AddMonths(1));

        issue.PlacementClosesAtUtc.Should().Be(Closes.AddMonths(1));
        issue.PlacementExtensionCount.Should().Be(1);
    }

    [Fact]
    public void ExtendPlacement_CountsEveryExtensionSeparately()
    {
        // The count is what makes a placement extended four times distinguishable afterwards from one
        // extended once.
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        issue.ExtendPlacement(Closes.AddMonths(1));
        issue.ExtendPlacement(Closes.AddMonths(2));

        issue.PlacementExtensionCount.Should().Be(2);
    }

    [Fact]
    public void ExtendPlacement_RefusesToMoveTheDateEarlier()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes);

        var act = () => issue.ExtendPlacement(Closes.AddDays(-1));

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void ExtendPlacement_RefusesWhenThereIsNoClosingDate()
    {
        var issue = NewIssue();

        var act = () => issue.ExtendPlacement(Closes);

        act.Should().Throw<DomainException>();
    }
}
