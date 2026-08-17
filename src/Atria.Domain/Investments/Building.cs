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
