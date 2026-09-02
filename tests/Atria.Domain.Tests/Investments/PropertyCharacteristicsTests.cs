using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// The descriptive half of the object card — "заполняется тем, что есть". Every field is optional
/// and free text, because "монолит-кирпич" and "2 пассажирских, 1 грузовой" are the answers people
/// actually give and a fixed list would force the nearest wrong one.
/// <para>
/// The one rule with teeth is the areas: usable space cannot exceed the floor it sits on, and it is
/// checked from both directions — otherwise editing the total downwards would quietly leave a unit
/// with more usable area than it has floor.
/// </para>
/// </summary>
public sealed class PropertyCharacteristicsTests
{
    private static Property NewUnit()
    {
        var unit = Property.Create("Квартира 12", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");
        unit.SetUnitDetails(UnitType.Apartment, totalAreaSqM: 80m);
        return unit;
    }

    [Fact]
    public void SetCharacteristics_recordsWhateverIsKnown()
    {
        var unit = NewUnit();

        unit.SetCharacteristics(
            usableAreaSqM: 72.5m, documentedUse: "жилое", buildingClass: "комфорт",
            wallMaterial: "монолит-кирпич", heating: "центральное",
            elevator: "2 пассажирских, 1 грузовой", security: "круглосуточная",
            parking: "подземный паркинг, 120 мест");

        unit.UsableAreaSqM.Should().Be(72.5m);
        unit.DocumentedUse.Should().Be("жилое");
        unit.BuildingClass.Should().Be("комфорт");
        unit.WallMaterial.Should().Be("монолит-кирпич");
        unit.Heating.Should().Be("центральное");
        unit.Elevator.Should().Be("2 пассажирских, 1 грузовой");
        unit.Security.Should().Be("круглосуточная");
        unit.Parking.Should().Be("подземный паркинг, 120 мест");
    }

    [Fact]
    public void EverythingIsUnknownUntilSomeoneFillsItIn()
    {
        // "Заполняется тем, что есть" — а на первом объекте нет ничего.
        var unit = NewUnit();

        unit.UsableAreaSqM.Should().BeNull();
        unit.DocumentedUse.Should().BeNull();
        unit.BuildingClass.Should().BeNull();
        unit.Parking.Should().BeNull();
    }

    [Fact]
    public void SetCharacteristics_patchesOneFieldAtATime()
    {
        var unit = NewUnit();
        unit.SetCharacteristics(buildingClass: "бизнес", heating: "центральное");

        unit.SetCharacteristics(parking: "наземная стоянка");

        unit.BuildingClass.Should().Be("бизнес");
        unit.Heating.Should().Be("центральное");
        unit.Parking.Should().Be("наземная стоянка");
    }

    [Fact]
    public void SetCharacteristics_treatsBlankAsAbsent()
    {
        var unit = NewUnit();
        unit.SetCharacteristics(heating: "центральное");

        unit.SetCharacteristics(heating: "   ");

        unit.Heating.Should().Be("центральное");
    }

    [Fact]
    public void UsableArea_cannotExceedTheTotalArea()
    {
        var unit = NewUnit();

        var act = () => unit.SetCharacteristics(usableAreaSqM: 90m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void TotalArea_cannotBeEditedBelowTheUsableArea()
    {
        // The same rule from the other side: without it, shrinking the total would leave the unit
        // claiming more usable space than it has floor.
        var unit = NewUnit();
        unit.SetCharacteristics(usableAreaSqM: 72m);

        var act = () => unit.SetUnitDetails(totalAreaSqM: 70m);

        act.Should().Throw<DomainException>();
        unit.TotalAreaSqM.Should().Be(80m);
    }

    [Fact]
    public void UsableArea_maySitOnAUnitWhoseTotalIsNotKnownYet()
    {
        // A plot or a unit still being described has no total area to compare against; refusing the
        // usable figure until the total arrives would just lose it.
        var plot = Property.Create("Участок", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

        plot.SetCharacteristics(usableAreaSqM: 500m);

        plot.UsableAreaSqM.Should().Be(500m);
    }

    [Fact]
    public void UsableArea_mustBePositive()
    {
        var unit = NewUnit();

        var act = () => unit.SetCharacteristics(usableAreaSqM: 0m);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void DocumentedUse_isSeparateFromTheCatalogueType()
    {
        // What the documents allow and how the object is marketed are different claims, and only one
        // of them is legally binding.
        var unit = NewUnit();
        unit.UpdateDetails(propertyType: "Коммерческая недвижимость");

        unit.SetCharacteristics(documentedUse: "нежилое — офис");

        unit.PropertyType.Should().Be("Коммерческая недвижимость");
        unit.DocumentedUse.Should().Be("нежилое — офис");
    }
}
