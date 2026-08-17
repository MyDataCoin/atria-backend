using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// One line of a unit's room breakdown — "Кухня+Столовая — 28,68 м²". Child entity of the
/// <see cref="Property"/> aggregate. Deliberately a free-text name plus an area rather than fixed
/// kitchen/bathroom/bedroom columns: the same list has to describe a 2-room flat, a 4-room
/// apartment with two bathrooms, and a garage.
/// </summary>
public sealed class PropertyRoom : Entity
{
    public Guid PropertyId { get; private set; }

    /// <summary>Room label as the admin typed it (e.g. "Кухня+Столовая", "Лоджия", "Спальня 2").</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Floor area of this room in square metres.</summary>
    public decimal AreaSqM { get; private set; }

    /// <summary>Position in the list, so the breakdown displays in the order it was entered.</summary>
    public int SortOrder { get; private set; }

    private PropertyRoom() { }

    internal static PropertyRoom Create(Guid propertyId, string name, decimal areaSqM, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Room name is required.");
        if (areaSqM <= 0)
            throw new DomainException("Room area must be positive.");

        return new PropertyRoom
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Name = name.Trim(),
            AreaSqM = areaSqM,
            SortOrder = sortOrder
        };
    }
}
