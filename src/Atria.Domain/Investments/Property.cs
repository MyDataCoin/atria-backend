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

    // Descriptive characteristics captured on creation (admin form). All optional.
    public string? PropertyType { get; private set; }
    public string? City { get; private set; }
    public int? YearBuilt { get; private set; }
    public string? Developer { get; private set; }
    public int? Floors { get; private set; }

    public decimal TotalValue { get; private set; }
    public decimal TokenPrice { get; private set; }
    public long TotalTokens { get; private set; }
    public long AvailableTokens { get; private set; }
    public string Currency { get; private set; } = null!;

    // --- On-chain issuance (each property is its own registered issuance / permissioned contract) ---

    /// <summary>Address of this issuance's permissioned token contract on <see cref="TokenChain"/>. Null until deployed.</summary>
    public string? TokenContractAddress { get; private set; }

    /// <summary>Chain the token contract lives on (e.g. the BNB Chain id). Null until deployed.</summary>
    public string? TokenChain { get; private set; }

    /// <summary>Issuer wallet that holds/mints the issuance. Null until set.</summary>
    public string? IssuerWalletAddress { get; private set; }

    // Persisted status enum; the current state is derived from it on demand (EF-friendly).
    public PropertyStatus Status { get; private set; }

    /// <summary>
    /// Whether new purchases are paused. Orthogonal to <see cref="Status"/>: an admin can freeze
    /// buying on an open offering without changing its lifecycle. The public site blocks "buy" while
    /// this is true, and <see cref="Atria.Domain.Factories.InvestmentFactory"/> callers reject new
    /// investments.
    /// </summary>
    public bool SalesPaused { get; private set; }

    private readonly List<PropertyImage> _images = new();
    public IReadOnlyCollection<PropertyImage> Images => _images.AsReadOnly();

    private readonly List<PropertyDocument> _documents = new();
    public IReadOnlyCollection<PropertyDocument> Documents => _documents.AsReadOnly();

    // private ctor: creation only through the factory method
    private Property() { }

    public static Property Create(
        string name, string? description, string? address, decimal totalValue,
        decimal tokenPrice, long totalTokens, string currency,
        string? propertyType = null, string? city = null, int? yearBuilt = null,
        string? developer = null, int? floors = null)
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
            PropertyType = propertyType,
            City = city,
            YearBuilt = yearBuilt,
            Developer = developer,
            Floors = floors,
            TotalValue = totalValue,
            TokenPrice = tokenPrice,
            TotalTokens = totalTokens,
            AvailableTokens = totalTokens, // full supply available at creation
            Currency = currency,
            Status = PropertyStatus.Draft // created as a draft; goes live via Publish()
        };
    }

    /// <summary>
    /// Edits the property's descriptive details. Only non-null arguments are applied, so a caller can
    /// PATCH a single field. Economics (total value, token price/supply, currency) and the lifecycle
    /// status are NOT editable here — changing them after investors have bought in would rewrite the
    /// terms of an existing offering.
    /// </summary>
    public void UpdateDetails(
        string? name = null, string? description = null, string? address = null,
        string? propertyType = null, string? city = null, int? yearBuilt = null,
        string? developer = null, int? floors = null)
    {
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException("Property name is required.");
            Name = name;
        }

        Description = description ?? Description;
        Address = address ?? Address;
        PropertyType = propertyType ?? PropertyType;
        City = city ?? City;
        YearBuilt = yearBuilt ?? YearBuilt;
        Developer = developer ?? Developer;
        Floors = floors ?? Floors;
    }

    /// <summary>
    /// Announces the property as "coming soon" (Draft or Open -> ComingSoon). Can tease a new draft
    /// or pull an already-open property back off the market into "coming soon".
    /// </summary>
    public void Announce()
        => Status = PropertyStateFactory.Create(Status).Announce(this).Status;

    /// <summary>Reverses an announcement (ComingSoon -> Draft), hiding the property from the site again.</summary>
    public void Unannounce()
        => Status = PropertyStateFactory.Create(Status).Unannounce(this).Status;

    /// <summary>Freezes new purchases (orthogonal to the lifecycle status).</summary>
    public void PauseSales() => SalesPaused = true;

    /// <summary>Resumes new purchases.</summary>
    public void ResumeSales() => SalesPaused = false;

    /// <summary>
    /// Publishes the property, opening it to investors (Draft or ComingSoon -> Open). A property can
    /// be published straight from draft, or after being teased as "coming soon".
    /// </summary>
    public void Publish()
        => Status = PropertyStateFactory.Create(Status).Publish(this).Status;

    /// <summary>Completes the property's offering (Open -> Completed). Terminal.</summary>
    public void Complete()
        => Status = PropertyStateFactory.Create(Status).Complete(this).Status;

    /// <summary>
    /// Holds <paramref name="count"/> tokens from the available supply for a new application. This is
    /// the authoritative point where capacity is claimed (at application time), so the offering cannot
    /// be oversubscribed by concurrent applications racing on the last tokens.
    /// </summary>
    public void ReserveTokens(long count)
    {
        if (count <= 0)
            throw new DomainException("Token reservation count must be positive.");
        if (count > AvailableTokens)
            throw new DomainException("Cannot reserve more tokens than are available.");

        AvailableTokens -= count;
    }

    /// <summary>
    /// Returns <paramref name="count"/> previously reserved tokens to the available supply when an
    /// application is rejected, cancelled, or its reservation lapses.
    /// </summary>
    public void ReleaseTokens(long count)
    {
        if (count <= 0)
            throw new DomainException("Token release count must be positive.");
        if (AvailableTokens + count > TotalTokens)
            throw new DomainException("Cannot release more tokens than the total supply.");

        AvailableTokens += count;
    }

    /// <summary>Records this issuance's on-chain token contract, chain and issuer wallet.</summary>
    public void SetTokenContract(string tokenContractAddress, string tokenChain, string issuerWalletAddress)
    {
        if (string.IsNullOrWhiteSpace(tokenContractAddress))
            throw new DomainException("Token contract address is required.");
        if (string.IsNullOrWhiteSpace(tokenChain))
            throw new DomainException("Token chain is required.");
        if (string.IsNullOrWhiteSpace(issuerWalletAddress))
            throw new DomainException("Issuer wallet address is required.");

        TokenContractAddress = tokenContractAddress;
        TokenChain = tokenChain;
        IssuerWalletAddress = issuerWalletAddress;
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
