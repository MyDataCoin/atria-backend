using Atria.Domain.Common;
using Atria.Domain.Investments;
using FluentAssertions;

namespace Atria.Domain.Tests.Investments;

/// <summary>
/// Covers where a garage / parking space sits in the car park: section, row and spot.
/// <para>
/// The three are strings rather than numbers, and — unlike every other unit field — <c>null</c>
/// CLEARS them instead of meaning "leave as is". Both choices are load-bearing, so both are pinned
/// here: a numeric type would drop the letter in "12А", and "null = leave as is" would strand a
/// parking row on a unit the admin has since turned into an apartment.
/// </para>
/// </summary>
public sealed class PropertyParkingAddressTests
{
    private static Property NewSpace() => Property.Create(
        "Парковочное место P-125", null, null, 1_000_000m, 1_000m, 1_000L, "KGS");

    [Fact]
    public void SetParkingAddress_KeepsTheAddressAsWritten()
    {
        var space = NewSpace();

        space.SetParkingAddress("B", "12", "125");

        space.Section.Should().Be("B");
        space.Row.Should().Be("12");
        space.Spot.Should().Be("125");
    }

    [Fact]
    public void SetParkingAddress_KeepsLettersInSectionAndRow()
    {
        // The reason these are strings: parsing "12А" as a number either fails or silently drops the
        // letter, and both outcomes lose the address the admin actually has.
        var space = NewSpace();

        space.SetParkingAddress("Б", "12А", "125");

        space.Section.Should().Be("Б");
        space.Row.Should().Be("12А");
    }

    [Fact]
    public void SetParkingAddress_AllowsFillingOnlyPartOfIt()
    {
        // A car park may number its spaces without rows; each part stands on its own.
        var space = NewSpace();

        space.SetParkingAddress(null, null, "125");

        space.Section.Should().BeNull();
        space.Row.Should().BeNull();
        space.Spot.Should().Be("125");
    }

    [Fact]
    public void SetParkingAddress_NullClearsAPreviouslyEnteredAddress()
    {
        // The switch-the-unit-type case: the admin typed a section, then made the unit an apartment.
        // The form sends all three as null and the address has to go with it.
        var space = NewSpace();
        space.SetParkingAddress("B", "12", "125");

        space.SetParkingAddress(null, null, null);

        space.Section.Should().BeNull();
        space.Row.Should().BeNull();
        space.Spot.Should().BeNull();
    }

    [Fact]
    public void SetParkingAddress_TreatsBlankAsUnset()
    {
        // An admin who clears the input sends "", not null; storing that would print an empty
        // section as a value instead of as "not set".
        var space = NewSpace();

        space.SetParkingAddress("  ", "\t", "125");

        space.Section.Should().BeNull();
        space.Row.Should().BeNull();
        space.Spot.Should().Be("125");
    }

    [Fact]
    public void SetParkingAddress_TrimsSurroundingWhitespace()
    {
        var space = NewSpace();

        space.SetParkingAddress(" B ", " 12 ", " 125 ");

        space.Section.Should().Be("B");
        space.Row.Should().Be("12");
        space.Spot.Should().Be("125");
    }

    [Fact]
    public void SetParkingAddress_RejectsAPartLongerThanTheColumn()
    {
        // The column is varchar(32); refusing here beats a truncation the admin never sees.
        var space = NewSpace();
        var tooLong = new string('B', Property.MaxParkingAddressPart + 1);

        var act = () => space.SetParkingAddress(tooLong, null, null);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void SetParkingAddress_LeavesTheRestOfTheUnitAlone()
    {
        var space = NewSpace();
        space.SetUnitDetails(UnitType.ParkingSpace, "P-125", -1, null, 15.2m);

        space.SetParkingAddress("B", "12", "125");

        space.UnitType.Should().Be(UnitType.ParkingSpace);
        space.UnitNumber.Should().Be("P-125");
        space.FloorNumber.Should().Be(-1);
        space.RoomCount.Should().BeNull();
        space.TotalAreaSqM.Should().Be(15.2m);
    }

    [Fact]
    public void SetUnitDetails_AcceptsAnUndergroundFloorAndNoRooms()
    {
        // A parking space lives on floor -1 and is sold with no rooms at all; neither is an error.
        var space = NewSpace();

        space.SetUnitDetails(UnitType.ParkingSpace, "P-125", -1, null, 15.2m);

        space.FloorNumber.Should().Be(-1);
        space.RoomCount.Should().BeNull();
        space.Rooms.Should().BeEmpty();
    }
}
