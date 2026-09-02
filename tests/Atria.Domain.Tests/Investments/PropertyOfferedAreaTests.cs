using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers the area an issue is cut from, and the metres one share stands for.
/// <para>
/// An issue does not always cover the whole object: the first placement is 10 000 m² of a building
/// designed on a 0.72 ha plot — more metres than the plot has, and more than any floor area that
/// exists yet. What a share stands for has to come from the area actually placed, and it has to
/// stop moving the moment someone has bought one.
/// </para>
/// </summary>
public sealed class PropertyOfferedAreaTests
{
    private static readonly DateTime Opens = new(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Closes = new(2027, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    // 10 000 shares: with 10 000 m² placed, one share is exactly one square metre.
    private static Property NewIssue() => Property.Create(
        "ЖК на Токомбаева", null, null, 10_000_000m, 1_000m, 10_000L, "KGS");

    [Fact]
    public void AreaPerToken_DividesTheAreaActuallyPlaced()
    {
        var issue = NewIssue();

        issue.SchedulePlacement(Opens, Closes, offeredAreaSqM: 10_000m);

        issue.OfferedAreaSqM.Should().Be(10_000m);
        issue.AreaPerTokenSqM.Should().Be(1m);
    }

    [Fact]
    public void AreaPerToken_FallsBackToTheFloorAreaWhenTheWholeUnitIsIssued()
    {
        var issue = NewIssue();
        issue.SetUnitDetails(totalAreaSqM: 5_000m);

        issue.OfferedAreaSqM.Should().BeNull();
        issue.AreaPerTokenSqM.Should().Be(0.5m);
    }

    [Fact]
    public void AreaPerToken_PrefersThePlacedAreaOverTheFloorArea()
    {
        var issue = NewIssue();
        issue.SetUnitDetails(totalAreaSqM: 5_000m);

        issue.SchedulePlacement(offeredAreaSqM: 10_000m);

        // Not 0.5: the issue is cut from the 10 000 m² being placed, not from the floor that exists.
        issue.AreaPerTokenSqM.Should().Be(1m);
    }

    [Fact]
    public void AreaPerToken_IsNullForAPlotWithNoAreaToDivide()
    {
        var issue = NewIssue();
        // Hectares are not floor area and are never divided across the issue.
        issue.SetCadastralDetails(landAreaHectares: 0.72m);

        issue.AreaPerTokenSqM.Should().BeNull();
    }

    [Fact]
    public void SchedulePlacement_RefusesANonPositivePlacedArea()
    {
        var issue = NewIssue();

        var act = () => issue.SchedulePlacement(offeredAreaSqM: 0m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SchedulePlacement_LeavesThePlacedAreaAloneWhenItIsNotGiven()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(offeredAreaSqM: 10_000m);

        issue.SchedulePlacement(Opens, Closes, 5_000_000m);

        issue.OfferedAreaSqM.Should().Be(10_000m);
    }

    [Fact]
    public void SchedulePlacement_RefusesToChangeThePlacedAreaOnceSharesAreOut()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(offeredAreaSqM: 10_000m);
        issue.ReserveTokens(1);

        var act = () => issue.SchedulePlacement(offeredAreaSqM: 5_000m);

        // Halving it would leave the holder of that one share with half the metres they bought.
        act.Should().Throw<DomainException>();
        issue.OfferedAreaSqM.Should().Be(10_000m);
    }

    [Fact]
    public void SchedulePlacement_StillMovesTheWindowOnceSharesAreOut()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(Opens, Closes, offeredAreaSqM: 10_000m);
        issue.ReserveTokens(1);

        // Re-sending the SAME area is not a change, and the window is not frozen by a sale.
        var act = () => issue.SchedulePlacement(
            Opens, Closes.AddMonths(1), 5_000_000m, offeredAreaSqM: 10_000m);

        act.Should().NotThrow();
        issue.PlacementClosesAtUtc.Should().Be(Closes.AddMonths(1));
    }

    [Fact]
    public void SchedulePlacement_AllowsCorrectingThePlacedAreaBeforeAnythingIsSold()
    {
        var issue = NewIssue();
        issue.SchedulePlacement(offeredAreaSqM: 12_000m);

        issue.SchedulePlacement(offeredAreaSqM: 10_000m);

        issue.OfferedAreaSqM.Should().Be(10_000m);
    }
}
