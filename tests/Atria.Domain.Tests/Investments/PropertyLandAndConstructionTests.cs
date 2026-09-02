using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers an issue whose object is not built: a land plot under design. The first object is exactly
/// this — 0.72 ha on пр. А.Токомбаева, cadastre code 1-04-13-0033-0135, nothing standing on it.
/// <para>
/// Two separations are load-bearing here and both are pinned. Land area is hectares and never
/// becomes floor area, because <see cref="Property.AreaPerTokenSqM"/> divides floor area across the
/// issue and would otherwise report a share of a plot in square metres it does not have. And the
/// construction stage never touches the placement status, because a placement legitimately opens
/// while the site is still a drawing.
/// </para>
/// </summary>
public sealed class PropertyLandAndConstructionTests
{
    private static Property NewPlot() => Property.Create(
        "Земельный участок, пр. А.Токомбаева 37/6", null, null, 100_000_000m, 1_000m, 100_000L, "KGS");

    [Fact]
    public void SetCadastralDetails_KeepsThePlotCodeAsWritten()
    {
        var plot = NewPlot();

        plot.SetCadastralDetails(landPlotCode: "1-04-13-0033-0135", landAreaHectares: 0.72m);

        plot.LandPlotCode.Should().Be("1-04-13-0033-0135");
        plot.LandAreaHectares.Should().Be(0.72m);
    }

    [Fact]
    public void SetCadastralDetails_LeavesFloorAreaAlone()
    {
        // The separation that matters: hectares of land are not square metres of sellable floor.
        var plot = NewPlot();

        plot.SetCadastralDetails(landAreaHectares: 0.72m);

        plot.TotalAreaSqM.Should().BeNull();
        plot.AreaPerTokenSqM.Should().BeNull("a plot has no floor area to divide across the issue");
    }

    [Fact]
    public void SetCadastralDetails_PatchesOneFieldAtATime()
    {
        var plot = NewPlot();
        plot.SetCadastralDetails(landPlotCode: "1-04-13-0033-0135", landAreaHectares: 0.72m);

        plot.SetCadastralDetails(cadastralNumber: "5-11-02-0001-0007");

        plot.LandPlotCode.Should().Be("1-04-13-0033-0135");
        plot.LandAreaHectares.Should().Be(0.72m);
        plot.CadastralNumber.Should().Be("5-11-02-0001-0007");
    }

    [Fact]
    public void SetCadastralDetails_TreatsBlankAsAbsent()
    {
        // An admin who clears the input sends "", and storing that would print an empty code as a
        // value rather than as "not set".
        var plot = NewPlot();
        plot.SetCadastralDetails(landPlotCode: "1-04-13-0033-0135");

        plot.SetCadastralDetails(landPlotCode: "   ");

        plot.LandPlotCode.Should().Be("1-04-13-0033-0135");
    }

    [Fact]
    public void SetCadastralDetails_RejectsANonPositiveLandArea()
    {
        var plot = NewPlot();

        var act = () => plot.SetCadastralDetails(landAreaHectares: 0m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetConstructionProgress_RecordsTheStageAndSchedule()
    {
        var plot = NewPlot();

        plot.SetConstructionProgress(
            ConstructionStage.Design, new DateTime(2029, 12, 31, 0, 0, 0, DateTimeKind.Utc), 0);

        plot.ConstructionStage.Should().Be(ConstructionStage.Design);
        plot.PlannedCompletionDate.Should().Be(new DateTime(2029, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        plot.ReadinessPercent.Should().Be(0);
    }

    [Fact]
    public void SetConstructionProgress_LeavesThePlacementStatusAlone()
    {
        // The whole point of a separate stage: an issue can be open for subscription while the site
        // is still a design on paper.
        var plot = NewPlot();
        plot.Publish();

        plot.SetConstructionProgress(ConstructionStage.Design);

        plot.Status.Should().Be(PropertyStatus.Open);
        plot.SalesPaused.Should().BeFalse();
    }

    [Fact]
    public void SetConstructionProgress_TreatsUnspecifiedAsLeaveAsIs()
    {
        var plot = NewPlot();
        plot.SetConstructionProgress(ConstructionStage.Design);

        plot.SetConstructionProgress(ConstructionStage.Unspecified, readinessPercent: 5);

        plot.ConstructionStage.Should().Be(ConstructionStage.Design);
        plot.ReadinessPercent.Should().Be(5);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void SetConstructionProgress_RejectsReadinessOutsideZeroToHundred(int percent)
    {
        var plot = NewPlot();

        var act = () => plot.SetConstructionProgress(readinessPercent: percent);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void EncumbranceCheck_IsUnknownUntilOneIsRecorded()
    {
        // "Nobody has looked" must stay distinguishable from "looked, and it is clean" — which is
        // why this is a nullable bool and not a flag defaulting to false.
        var plot = NewPlot();

        plot.IsFreeOfEncumbrances.Should().BeNull();
        plot.EncumbranceCheckedAtUtc.Should().BeNull();
    }

    [Fact]
    public void RecordEncumbranceCheck_StoresTheVerdictWithItsDate()
    {
        var plot = NewPlot();
        var checkedAt = new DateTime(2027, 7, 22, 0, 0, 0, DateTimeKind.Utc);

        plot.RecordEncumbranceCheck(isFree: true, checkedAt);

        plot.IsFreeOfEncumbrances.Should().BeTrue();
        plot.EncumbranceCheckedAtUtc.Should().Be(checkedAt);
    }

    [Fact]
    public void RecordEncumbranceCheck_IsSeparateFromThePledgeWeRegister()
    {
        // A clean cadastre check says no third party has a claim. The encumbrance WE register in
        // favour of the issue is a different record, and one must not overwrite the other.
        var plot = NewPlot();
        plot.RecordEncumbranceCheck(isFree: true, DateTime.UtcNow);

        plot.SetEncumbrance("ЗАЛ-2027-001", DateTime.UtcNow);

        plot.IsFreeOfEncumbrances.Should().BeTrue();
        plot.EncumbranceRegistrationNumber.Should().Be("ЗАЛ-2027-001");
    }
}
