namespace Atria.Application.Properties.Dtos;

/// <summary>
/// One row of the room breakdown as the admin enters it — a free-text label and its area in m².
/// The list is sent whole (create) or replaced whole (update); there is no per-row endpoint.
/// </summary>
/// <param name="Name">Room label, e.g. "Кухня+Столовая", "Прихожая", "Лоджия".</param>
/// <param name="AreaSqM">Floor area of the room in square metres; must be positive.</param>
public sealed record PropertyRoomInput(string Name, decimal AreaSqM);
