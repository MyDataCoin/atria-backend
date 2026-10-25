using Atria.Domain.Common;

namespace Atria.Domain.Investments;

/// <summary>
/// A building — the physical object an admin registers first. It groups the units sold inside it
/// (apartments, garages, parking spaces), each of which is a <see cref="Property"/> with its own
/// token issue. The building itself never issues tokens: it carries only the shared descriptive
/// data (address, developer, year, floors, photos of the building) that its units inherit for
/// display, so an admin types it once instead of on every apartment.
/// </summary>
public sealed class Building : AggregateRoot
{
    /// <summary>Maximum photos a building may have.</summary>
    public const int MaxImages = 10;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Address { get; private set; }
    public string? City { get; private set; }
    public string? Developer { get; private set; }
    public int? YearBuilt { get; private set; }
    public int? Floors { get; private set; }

    /// <summary>Kind of building (e.g. residential, commercial, mixed). Free text, optional.</summary>
    public string? BuildingType { get; private set; }

    /// <summary>
    /// Total floor area of the whole building, in m². Not the sum of the units on sale: a business
    /// centre places its offices, while the area also counts the parking, the lobby and the plant rooms.
    /// </summary>
    public decimal? TotalAreaSqM { get; private set; }

    // --- Construction readiness — the same meaning as on Property, stated once for the building ---

    /// <summary>Built or still being built. <see cref="ConstructionStage.Unspecified"/> until stated.</summary>
    public ConstructionStage ConstructionStage { get; private set; } = ConstructionStage.Unspecified;

    /// <summary>When the building is expected to be commissioned. Null while there is no schedule.</summary>
    public DateTime? PlannedCompletionDate { get; private set; }

    /// <summary>How precisely <see cref="PlannedCompletionDate"/> was stated.</summary>
    public CompletionDatePrecision PlannedCompletionPrecision { get; private set; } = CompletionDatePrecision.Day;

    // --- Amenities. Ticked = the building has it (or the design provides for it); unticked = not stated ---

    public bool HasElevator { get; private set; }
    public bool HasHeating { get; private set; }
    public bool HasSecurity { get; private set; }
    public bool HasParking { get; private set; }

    // --- Cadastre check: the same two-field shape as on Property, for the same reason ---

    /// <summary>True when free of encumbrances and arrests, false when something was found, null when nobody looked.</summary>
    public bool? IsFreeOfEncumbrances { get; private set; }

    /// <summary>When the check behind <see cref="IsFreeOfEncumbrances"/> was made.</summary>
    public DateTime? EncumbranceCheckedAtUtc { get; private set; }

    private readonly List<BuildingImage> _images = new();
    public IReadOnlyCollection<BuildingImage> Images => _images.AsReadOnly();

    // private ctor: creation only through the factory method
    private Building() { }

    public static Building Create(
        string name, string? description = null, string? address = null, string? city = null,
        string? developer = null, int? yearBuilt = null, int? floors = null, string? buildingType = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Building name is required.");

        return new Building
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Address = address,
            City = city,
            Developer = developer,
            YearBuilt = yearBuilt,
            Floors = floors,
            BuildingType = buildingType
        };
    }

    /// <summary>
    /// Edits the building's details. Only non-null arguments are applied, so a caller can PATCH a
    /// single field.
    /// </summary>
    public void UpdateDetails(
        string? name = null, string? description = null, string? address = null, string? city = null,
        string? developer = null, int? yearBuilt = null, int? floors = null, string? buildingType = null)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Building name is required.");
            Name = name;
        }

        Description = description ?? Description;
        Address = address ?? Address;
        City = city ?? City;
        Developer = developer ?? Developer;
        YearBuilt = yearBuilt ?? YearBuilt;
        Floors = floors ?? Floors;
        BuildingType = buildingType ?? BuildingType;
    }

    /// <summary>
    /// Whether a building with units in <paramref name="unitStatuses"/> may still be edited: only
    /// while every unit is a draft or announced. Once any unit is on sale (or past it), the building
    /// data on its card is part of what investors bought on, and it stops changing.
    /// </summary>
    public static bool IsEditableWith(IEnumerable<PropertyStatus> unitStatuses)
        => unitStatuses.All(s => s is PropertyStatus.Draft or PropertyStatus.ComingSoon);

    /// <summary>
    /// The text a unit inherits for a ticked amenity: "Предусмотрено проектом" while the building is
    /// being built, "Есть" once it stands; null when the box is not ticked.
    /// </summary>
    public string? AmenityText(bool ticked)
        => !ticked ? null
            : ConstructionStage is ConstructionStage.LandOnly or ConstructionStage.Design
                or ConstructionStage.UnderConstruction ? "Предусмотрено проектом" : "Есть";

    /// <summary>
    /// Records the building-wide characteristics: total area and amenities. Only non-null arguments
    /// are applied, so a caller can PATCH a single one.
    /// </summary>
    public void SetCharacteristics(
        decimal? totalAreaSqM = null, bool? hasElevator = null, bool? hasHeating = null,
        bool? hasSecurity = null, bool? hasParking = null)
    {
        if (totalAreaSqM is <= 0)
            throw new DomainException("Building area must be positive.");

        TotalAreaSqM = totalAreaSqM ?? TotalAreaSqM;
        HasElevator = hasElevator ?? HasElevator;
        HasHeating = hasHeating ?? HasHeating;
        HasSecurity = hasSecurity ?? HasSecurity;
        HasParking = hasParking ?? HasParking;
    }

    /// <summary>
    /// Records whether the building stands or is being built, and when it is due. Only non-null
    /// arguments are applied; <see cref="ConstructionStage.Unspecified"/> is "leave as is".
    /// </summary>
    public void SetConstructionProgress(
        ConstructionStage? stage = null, DateTime? plannedCompletionDate = null,
        CompletionDatePrecision? plannedCompletionPrecision = null)
    {
        if (stage is not null and not ConstructionStage.Unspecified)
            ConstructionStage = stage.Value;

        PlannedCompletionDate = plannedCompletionDate ?? PlannedCompletionDate;
        PlannedCompletionPrecision = plannedCompletionPrecision ?? PlannedCompletionPrecision;

        // A building that stands has no completion still to come.
        if (ConstructionStage == ConstructionStage.Commissioned)
        {
            PlannedCompletionDate = null;
            PlannedCompletionPrecision = CompletionDatePrecision.Day;
        }
    }

    /// <summary>Records a cadastre check for encumbrances and arrests: what was found and when.</summary>
    public void RecordEncumbranceCheck(bool isFree, DateTime checkedAtUtc)
    {
        IsFreeOfEncumbrances = isFree;
        EncumbranceCheckedAtUtc = checkedAtUtc;
    }

    /// <summary>Adds a photo (max <see cref="MaxImages"/>). Returns the created child.</summary>
    public BuildingImage AddImage(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Image URL is required.");
        if (_images.Count >= MaxImages)
            throw new DomainException($"A building can have at most {MaxImages} images.");

        var image = BuildingImage.Create(Id, url);
        _images.Add(image);
        return image;
    }

    /// <summary>Removes a photo by id; returns the removed child (with its URL) or null if not found.</summary>
    public BuildingImage? RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is not null)
            _images.Remove(image);
        return image;
    }
}
