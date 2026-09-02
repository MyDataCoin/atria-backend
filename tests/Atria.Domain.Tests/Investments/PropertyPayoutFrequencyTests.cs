using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The management company answered "выплаты ежемесячно" for an object that is a design on paper.
/// Both halves of that are true — monthly is the intended schedule, and the object earns nothing yet
/// — so the issue records the intention and derives whether it actually distributes.
/// <para>
/// This matters because the offering page reads the derived flag. Printing "ежемесячно" beside a
/// building that does not exist promises investors a payment the issue cannot make.
/// </para>
/// </summary>
public sealed class PropertyPayoutFrequencyTests
{
    private static Property NewIssue() => Property.Create(
        "ЖК на Токомбаева", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void AnIssueStartsWithNoStatedFrequency()
    {
        var issue = NewIssue();

        issue.PayoutFrequency.Should().Be(PayoutFrequency.Unspecified);
        issue.DistributesYet.Should().BeFalse();
    }

    [Fact]
    public void SetPayoutFrequency_recordsTheIntendedSchedule()
    {
        var issue = NewIssue();

        issue.SetPayoutFrequency(PayoutFrequency.Monthly);

        issue.PayoutFrequency.Should().Be(PayoutFrequency.Monthly);
    }

    [Fact]
    public void SetPayoutFrequency_treatsUnspecifiedAsLeaveAsIs()
    {
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.Quarterly);

        issue.SetPayoutFrequency(PayoutFrequency.Unspecified);

        issue.PayoutFrequency.Should().Be(PayoutFrequency.Quarterly);
    }

    [Fact]
    public void AnObjectUnderDesign_doesNotDistributeHoweverOftenItIntendsTo()
    {
        // The exact case of the first object: monthly was answered, nothing is built.
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.Monthly);
        issue.SetConstructionProgress(ConstructionStage.Design);

        issue.PayoutFrequency.Should().Be(PayoutFrequency.Monthly);
        issue.DistributesYet.Should().BeFalse("nothing is built, so nothing is earned");
    }

    [Fact]
    public void AnObjectUnderConstruction_doesNotDistributeEither()
    {
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.Monthly);
        issue.SetConstructionProgress(ConstructionStage.UnderConstruction, readinessPercent: 60);

        issue.DistributesYet.Should().BeFalse();
    }

    [Fact]
    public void AcommissionedObject_distributesOnItsStatedSchedule()
    {
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.Monthly);
        issue.SetConstructionProgress(ConstructionStage.Commissioned, readinessPercent: 100);

        issue.DistributesYet.Should().BeTrue();
    }

    [Fact]
    public void AnIssueThatPaysNothing_doesNotDistributeEvenOnceCommissioned()
    {
        // `None` is a stated answer, not a missing one, and it outranks the stage.
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.None);
        issue.SetConstructionProgress(ConstructionStage.Commissioned);

        issue.DistributesYet.Should().BeFalse();
    }

    [Fact]
    public void AnIssueWithNoStatedStage_distributesOnItsSchedule()
    {
        // Existing issues predate the stage field entirely; they must not silently stop distributing
        // just because nobody has said what stage they are at.
        var issue = NewIssue();
        issue.SetPayoutFrequency(PayoutFrequency.Quarterly);

        issue.ConstructionStage.Should().Be(ConstructionStage.Unspecified);
        issue.DistributesYet.Should().BeTrue();
    }
}
