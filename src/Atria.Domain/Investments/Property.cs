using Atria.Domain.Common;
using Atria.Domain.Investments.States;

namespace Atria.Domain.Investments;

/// <summary>
/// A real estate property that issues a fixed pool of tokens. Investors buy tokens
/// against the available supply.
/// </summary>
public sealed class Property : AggregateRoot
{
    /// <summary>Maximum photos a property may have.</summary>
    public const int MaxImages = 3;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Address { get; private set; }
    public decimal TotalValue { get; private set; }
    public decimal TokenPrice { get; private set; }
    public long TotalTokens { get; private set; }
    public long AvailableTokens { get; private set; }
    public string Currency { get; private set; } = null!;

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public PropertyStatus Status { get; private set; }

    private readonly List<PropertyImage> _images = new();
    public IReadOnlyCollection<PropertyImage> Images => _images.AsReadOnly();

    private readonly List<PropertyDocument> _documents = new();
    public IReadOnlyCollection<PropertyDocument> Documents => _documents.AsReadOnly();

    // private ctor: creation only through the factory method
    private Property() { }

    public static Property Create(
        string name, string? description, string? address, decimal totalValue,
        decimal tokenPrice, long totalTokens, string currency)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Property name is required.");
        if (totalValue <= 0)
            throw new DomainException("Property total value must be positive.");
        if (tokenPrice <= 0)
            throw new DomainException("Token price must be positive.");
        if (totalTokens <= 0)
            throw new DomainException("Total tokens must be positive.");
        if (string.IsNullOrWhiteSpace(currency))
            throw new DomainException("Currency is required.");

        return new Property
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Address = address,
            TotalValue = totalValue,
            TokenPrice = tokenPrice,
            TotalTokens = totalTokens,
            AvailableTokens = totalTokens, // full supply available at creation
            Currency = currency,
            Status = PropertyStatus.Draft // created as a draft; goes live via Publish()
        };
    }

    /// <summary>
    /// Announces the property as "coming soon" (Draft or Open -> ComingSoon). Can tease a new draft
    /// or pull an already-open property back off the market into "coming soon".
    /// </summary>
    public void Announce()
        => Status = PropertyStateFactory.Create(Status).Announce(this).Status;

    /// <summary>
    /// Publishes the property, opening it to investors (Draft or ComingSoon -> Open). A property can
    /// be published straight from draft, or after being teased as "coming soon".
    /// </summary>
    public void Publish()
        => Status = PropertyStateFactory.Create(Status).Publish(this).Status;

    /// <summary>Completes the property's offering (Open -> Completed). Terminal.</summary>
    public void Complete()
        => Status = PropertyStateFactory.Create(Status).Complete(this).Status;

    /// <summary>Reserves <paramref name="count"/> tokens, reducing the available supply.</summary>
    public void AllocateTokens(long count)
    {
        if (count <= 0)
            throw new DomainException("Token allocation count must be positive.");
        if (count > AvailableTokens)
            throw new DomainException("Cannot allocate more tokens than are available.");

        AvailableTokens -= count;
    }

    /// <summary>Adds a photo (max <see cref="MaxImages"/>). Returns the created child.</summary>
    public PropertyImage AddImage(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Image URL is required.");
        if (_images.Count >= MaxImages)
            throw new DomainException($"A property can have at most {MaxImages} images.");

        var image = PropertyImage.Create(Id, url);
        _images.Add(image);
        return image;
    }

    /// <summary>Removes a photo by id; returns the removed child (with its URL) or null if not found.</summary>
    public PropertyImage? RemoveImage(Guid imageId)
    {
        var image = _images.FirstOrDefault(i => i.Id == imageId);
        if (image is not null)
            _images.Remove(image);
        return image;
    }

    /// <summary>Adds a document. Returns the created child.</summary>
    public PropertyDocument AddDocument(string url, string fileName, string contentType)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new DomainException("Document URL is required.");

        var document = PropertyDocument.Create(Id, url, fileName, contentType);
        _documents.Add(document);
        return document;
    }

    /// <summary>Removes a document by id; returns the removed child (with its URL) or null if not found.</summary>
    public PropertyDocument? RemoveDocument(Guid documentId)
    {
        var document = _documents.FirstOrDefault(d => d.Id == documentId);
        if (document is not null)
            _documents.Remove(document);
        return document;
    }
}
