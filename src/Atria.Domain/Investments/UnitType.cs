namespace Atria.Domain.Investments;

/// <summary>
/// What kind of unit a <see cref="Property"/> is inside its <see cref="Building"/>. Persisted as an
/// int; the wire form is the lowercase snake_case name.
/// </summary>
public enum UnitType
{
    /// <summary>Not stated — a standalone issue that is not a unit of a building.</summary>
    Unspecified = 0,

    /// <summary>An apartment / flat.</summary>
    Apartment = 1,

    /// <summary>A garage box.</summary>
    Garage = 2,

    /// <summary>A parking space (open or underground).</summary>
    ParkingSpace = 3,

    /// <summary>A commercial space (shop, office).</summary>
    Commercial = 4,

    /// <summary>A storage room / basement unit.</summary>
    Storage = 5,

    /// <summary>
    /// A land plot. Not a unit of a building at all — the issue is the plot itself, sold before
    /// (or instead of) anything standing on it. Its area is <see cref="Property.LandAreaHectares"/>,
    /// not the floor area every other kind uses.
    /// </summary>
    LandPlot = 6,

    /// <summary>Anything else the admin needs to sell.</summary>
    Other = 99
}
