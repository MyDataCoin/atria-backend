namespace Atria.Application.Properties.Validators;

/// <summary>
/// Column widths of the free-text characteristics, mirrored from <c>PropertyConfiguration</c>.
/// </summary>
/// <remarks>
/// Without these the database is the only check, and an over-long value comes back as a bare
/// DbUpdateException — which the API reports as a 409 "conflict" that names no field. A pasted
/// paragraph about the district then reads as a concurrency bug somewhere else entirely.
/// </remarks>
internal static class PropertyFieldLimits
{
    public const int DocumentedUse = 256;
    public const int BuildingClass = 64;
    public const int WallMaterial = 128;
    public const int Heating = 128;
    public const int Elevator = 128;
    public const int Security = 128;
    public const int Parking = 256;
    public const int ManagementCompany = 256;
    public const int LocationDescription = 2048;
    public const int CadastreCode = 256;
}
