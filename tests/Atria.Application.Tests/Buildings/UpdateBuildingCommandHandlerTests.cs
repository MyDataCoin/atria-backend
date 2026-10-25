using Atria.Application.Abstractions;
using Atria.Application.Buildings.Commands;
using Atria.Domain.Investments;
using Atria.Domain.Users;
using FluentAssertions;
using NSubstitute;

namespace Atria.Application.Tests.Buildings;

/// <summary>
/// A building is edited only while its units are drafts or coming soon, and an edit reaches the
/// units through what they inherited — never through what was set on the unit itself.
/// </summary>
public sealed class UpdateBuildingCommandHandlerTests
{
    private readonly IBuildingRepository _buildings = Substitute.For<IBuildingRepository>();
    private readonly IPropertyRepository _properties = Substitute.For<IPropertyRepository>();
    private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private UpdateBuildingCommandHandler NewHandler()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(Role.Admin).Returns(true);
        return new UpdateBuildingCommandHandler(_buildings, _properties, _currentUser, _audit, _uow);
    }

    private Building GivenBuilding(params Property[] units)
    {
        var building = Building.Create("Офисы 7 мкр. д.41а", city: "Бишкек", buildingType: "Бизнес-центр");
        building.SetConstructionProgress(
            ConstructionStage.UnderConstruction, new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            CompletionDatePrecision.Quarter);
        building.SetCharacteristics(hasElevator: true);
        _buildings.GetByIdAsync(building.Id, Arg.Any<CancellationToken>()).Returns(building);

        foreach (var unit in units)
        {
            unit.AssignToBuilding(building.Id);
            _properties.GetByIdAsync(unit.Id, Arg.Any<CancellationToken>()).Returns(unit);
        }
        _properties.GetByBuildingAsync(building.Id, Arg.Any<CancellationToken>()).Returns(units);
        return building;
    }

    // A unit created through the form: it inherited the building's city, stage, date and lift.
    private static Property InheritingUnit()
    {
        var unit = Property.Create("Офис Г1", null, null, 1_000_000m, 1_000m, 1_000L, "KGS", city: "Бишкек");
        unit.SetConstructionProgress(
            ConstructionStage.UnderConstruction, new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            plannedCompletionPrecision: CompletionDatePrecision.Quarter);
        unit.SetCharacteristics(elevator: "Предусмотрено проектом");
        return unit;
    }

    [Fact]
    public async Task Edit_IsRefusedOnceAUnitIsOnSale()
    {
        var unit = InheritingUnit();
        unit.Publish();
        var building = GivenBuilding(unit);

        var result = await NewHandler().Handle(new UpdateBuildingCommand(building.Id, City: "Ош"), default);

        result.IsSuccess.Should().BeFalse();
        building.City.Should().Be("Бишкек");
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_IsAllowedWhileUnitsAreDraftOrComingSoon()
    {
        var announced = InheritingUnit();
        announced.Announce();
        var building = GivenBuilding(InheritingUnit(), announced);

        var result = await NewHandler().Handle(new UpdateBuildingCommand(building.Id, Floors: 10), default);

        result.IsSuccess.Should().BeTrue();
        building.Floors.Should().Be(10);
    }

    [Fact]
    public async Task Edit_MovesInheritedValuesToTheUnits()
    {
        var unit = InheritingUnit();
        var building = GivenBuilding(unit);

        await NewHandler().Handle(new UpdateBuildingCommand(
            building.Id, City: "Ош",
            PlannedCompletionDate: new DateTime(2028, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            HasElevator: false, HasParking: true), default);

        unit.City.Should().Be("Ош");
        unit.PlannedCompletionDate.Should().Be(new DateTime(2028, 3, 31, 0, 0, 0, DateTimeKind.Utc));
        unit.PlannedCompletionPrecision.Should().Be(CompletionDatePrecision.Quarter);
        unit.Elevator.Should().BeNull("the lift was inherited, and the building no longer has one ticked");
        unit.Parking.Should().Be("Предусмотрено проектом");
    }

    [Fact]
    public async Task Edit_LeavesValuesSetOnTheUnitItself()
    {
        var unit = InheritingUnit();
        unit.SetCharacteristics(elevator: "Два пассажирских");
        unit.SetConstructionProgress(plannedCompletionDate: new DateTime(2029, 6, 30, 0, 0, 0, DateTimeKind.Utc));
        var building = GivenBuilding(unit);

        await NewHandler().Handle(new UpdateBuildingCommand(
            building.Id,
            PlannedCompletionDate: new DateTime(2028, 3, 31, 0, 0, 0, DateTimeKind.Utc),
            HasElevator: false), default);

        unit.Elevator.Should().Be("Два пассажирских");
        unit.PlannedCompletionDate.Should().Be(new DateTime(2029, 6, 30, 0, 0, 0, DateTimeKind.Utc));
    }
}
