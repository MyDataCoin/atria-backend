using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers the building/unit model: a building groups units but issues nothing itself, and a
/// property carries the unit description (type, number, floor, area) plus its room breakdown.
/// </summary>
public sealed class BuildingUnitTests
{
    private static Property NewUnit() => Property.Create(
        "Апартамент 12", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void Create_RequiresAName()
    {
        var act = () => Building.Create("  ");
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void Create_KeepsTheSharedDescriptiveData()
    {
        var building = Building.Create(
            "ЖК Ала-Тоо", "блок B", "Эркиндик 12", "Бишкек", "Ala-Too Development", 2019, 9, "residential");

        building.Name.Should().Be("ЖК Ала-Тоо");
        building.Address.Should().Be("Эркиндик 12");
        building.City.Should().Be("Бишкек");
        building.Developer.Should().Be("Ala-Too Development");
        building.YearBuilt.Should().Be(2019);
        building.Floors.Should().Be(9);
        building.BuildingType.Should().Be("residential");
        building.Images.Should().BeEmpty();
    }

    [Fact]
    public void UpdateDetails_AppliesOnlyTheSuppliedFields()
    {
        var building = Building.Create("ЖК Ала-Тоо", city: "Бишкек", floors: 9);

        building.UpdateDetails(floors: 12);

        building.Name.Should().Be("ЖК Ала-Тоо");
        building.City.Should().Be("Бишкек");
        building.Floors.Should().Be(12);
    }

    [Fact]
    public void AddImage_StopsAtTheLimit()
    {
        var building = Building.Create("ЖК Ала-Тоо");
        for (var i = 0; i < Building.MaxImages; i++)
            building.AddImage($"/media/images/{i}.jpg");

        var act = () => building.AddImage("/media/images/overflow.jpg");

        act.Should().Throw<DomainException>();
        building.Images.Should().HaveCount(Building.MaxImages);
    }

    [Fact]
    public void RemoveImage_ReturnsTheRemovedChild_SoItsFileCanBeDeleted()
    {
        var building = Building.Create("ЖК Ала-Тоо");
        var image = building.AddImage("/media/images/a.jpg");

        building.RemoveImage(image.Id)!.Url.Should().Be("/media/images/a.jpg");
        building.Images.Should().BeEmpty();
        building.RemoveImage(Guid.NewGuid()).Should().BeNull();
    }

    [Fact]
    public void NewProperty_IsStandalone_UntilAssignedToABuilding()
    {
        var unit = NewUnit();

        unit.BuildingId.Should().BeNull();
        unit.UnitType.Should().Be(UnitType.Unspecified);

        var buildingId = Guid.NewGuid();
        unit.AssignToBuilding(buildingId);
        unit.BuildingId.Should().Be(buildingId);

        unit.AssignToBuilding(null);
        unit.BuildingId.Should().BeNull();
    }

    [Fact]
    public void SetUnitDetails_RecordsWhatTheUnitIs()
    {
        var unit = NewUnit();

        unit.SetUnitDetails(UnitType.Apartment, "12", 4, 3, 128.82m);

        unit.UnitType.Should().Be(UnitType.Apartment);
        unit.UnitNumber.Should().Be("12");
        unit.FloorNumber.Should().Be(4);
        unit.RoomCount.Should().Be(3);
        unit.TotalAreaSqM.Should().Be(128.82m);
    }

    [Fact]
    public void SetUnitDetails_LeavesUnsuppliedFieldsAlone()
    {
        var unit = NewUnit();
        unit.SetUnitDetails(UnitType.Apartment, "12", 4, 3, 128.82m);

        // A PATCH of the area alone must not wipe the type or the number.
        unit.SetUnitDetails(totalAreaSqM: 130m);

        unit.UnitType.Should().Be(UnitType.Apartment);
        unit.UnitNumber.Should().Be("12");
        unit.TotalAreaSqM.Should().Be(130m);
    }

    [Fact]
    public void SetUnitDetails_RejectsNonsenseNumbers()
    {
        var unit = NewUnit();

        unit.Invoking(u => u.SetUnitDetails(roomCount: -1)).Should().Throw<DomainException>();
        unit.Invoking(u => u.SetUnitDetails(totalAreaSqM: 0m)).Should().Throw<DomainException>();
    }

    [Fact]
    public void ReplaceRooms_KeepsTheEnteredOrder()
    {
        var unit = NewUnit();

        unit.ReplaceRooms(new[]
        {
            ("Кухня+Столовая", 28.68m),
            ("Прихожая", 5.65m),
            ("Ванная", 4.67m),
            ("Спальня", 14.88m),
            ("Лоджия", 3.67m),
        });

        unit.Rooms.Select(r => r.Name).Should().ContainInOrder("Кухня+Столовая", "Прихожая", "Ванная", "Спальня", "Лоджия");
        unit.Rooms.Select(r => r.SortOrder).Should().ContainInOrder(0, 1, 2, 3, 4);
        unit.Rooms.Sum(r => r.AreaSqM).Should().Be(57.55m);
    }

    [Fact]
    public void ReplaceRooms_SwapsTheWholeList_AndAnEmptyListClearsIt()
    {
        var unit = NewUnit();
        unit.ReplaceRooms(new[] { ("Кухня", 12m), ("Спальня", 18m) });

        unit.ReplaceRooms(new[] { ("Гараж", 18.4m) });
        unit.Rooms.Should().ContainSingle().Which.Name.Should().Be("Гараж");

        unit.ReplaceRooms(Array.Empty<(string, decimal)>());
        unit.Rooms.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceRooms_RejectsAnEmptyNameOrANonPositiveArea()
    {
        var unit = NewUnit();

        unit.Invoking(u => u.ReplaceRooms(new[] { (" ", 12m) })).Should().Throw<DomainException>();
        unit.Invoking(u => u.ReplaceRooms(new[] { ("Кухня", 0m) })).Should().Throw<DomainException>();
    }

    [Fact]
    public void ReplaceRooms_DoesNotForceTheRoomsToMatchTheTotalArea()
    {
        // Плановая площадь и сумма комнат легитимно расходятся; сверку показывает UI, а не домен.
        var unit = NewUnit();
        unit.SetUnitDetails(UnitType.Apartment, totalAreaSqM: 57.55m);

        var act = () => unit.ReplaceRooms(new[] { ("Кухня", 12m) });

        act.Should().NotThrow();
        unit.TotalAreaSqM.Should().Be(57.55m);
    }
}
