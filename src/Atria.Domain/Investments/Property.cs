using Atria.Domain.Common;
using Atria.Domain.Investments.States;

namespace Atria.Domain.Investments;

/// <summary>
/// A real estate property that issues a fixed pool of tokens. Investors buy tokens
/// against the available supply.
/// <para>
/// This is the unit of issuance: one property, one token issue, one holder register. A property is
/// either standalone, or one unit inside a <see cref="Building"/> — an apartment or a garage that
/// the admin put up for sale on its own. The building groups such units and holds their shared
/// address/developer data; the tokens are always issued here, per unit.
/// </para>
/// </summary>
public sealed class Property : AggregateRoot
{
    /// <summary>Maximum photos a property may have.</summary>
    public const int MaxImages = 10;

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Address { get; private set; }

    // Descriptive characteristics captured on creation (admin form). All optional.
    public string? PropertyType { get; private set; }
    public string? City { get; private set; }
    public int? YearBuilt { get; private set; }
    public string? Developer { get; private set; }
    public int? Floors { get; private set; }

    // --- Unit inside a building (all null / Unspecified for a standalone issue) ---

    /// <summary>The building this unit belongs to, or null when the issue is standalone.</summary>
    public Guid? BuildingId { get; private set; }

    /// <summary>What the unit is: apartment, garage, parking space… <see cref="UnitType.Unspecified"/> when standalone.</summary>
    public UnitType UnitType { get; private set; } = UnitType.Unspecified;

    /// <summary>Unit designation within the building (flat number, garage box number).</summary>
    public string? UnitNumber { get; private set; }

    /// <summary>Floor the unit is on. Distinct from <see cref="Floors"/>, which counts storeys.</summary>
    public int? FloorNumber { get; private set; }

    /// <summary>Number of rooms the unit is sold as (2-, 3-, 4-комнатная). Null for a garage.</summary>
    public int? RoomCount { get; private set; }

    /// <summary>Total floor area of the unit in square metres.</summary>
    public decimal? TotalAreaSqM { get; private set; }

    public decimal TotalValue { get; private set; }
    public decimal TokenPrice { get; private set; }

    /// <summary>
    /// Shares the issue is cut into. A whole number: the token is indivisible, so a fractional issue
    /// could never be minted. Sized so <see cref="TokenPrice"/> stays small against
    /// <see cref="MinPurchaseTokens"/> × price — see <see cref="TokenAmount"/> for why.
    /// </summary>
    public long TotalTokens { get; private set; }

    /// <summary>Shares of <see cref="TotalTokens"/> not yet reserved or placed.</summary>
    public long AvailableTokens { get; private set; }

    /// <summary>
    /// Fewest shares one application may be for. The floor on entry: with a unit price small enough
    /// to make any sum expressible in whole tokens, this is what keeps a purchase from being a
    /// handful of somoni. At least 1.
    /// </summary>
    public long MinPurchaseTokens { get; private set; } = TokenAmount.Smallest;

    /// <summary>The money a minimum-size application costs — what the investor is told to bring.</summary>
    public decimal MinPurchaseAmount => TokenAmount.CostOf(MinPurchaseTokens, TokenPrice);

    /// <summary>
    /// Area one share stands for, or null when the unit's area is unknown. An equivalent shown
    /// beside a holding, never the unit of issue itself.
    /// </summary>
    public decimal? AreaPerTokenSqM
        => TotalAreaSqM is { } area && TotalTokens > 0 ? area / TotalTokens : null;

    public string Currency { get; private set; } = null!;

    // --- On-chain issuance (each property is its own registered issuance / permissioned contract) ---

    /// <summary>Address of this issuance's permissioned token contract on <see cref="TokenChain"/>. Null until deployed.</summary>
    public string? TokenContractAddress { get; private set; }

    /// <summary>Chain the token contract lives on (e.g. the BNB Chain id). Null until deployed.</summary>
    public string? TokenChain { get; private set; }

    /// <summary>Issuer wallet that holds/mints the issuance. Null until set.</summary>
    public string? IssuerWalletAddress { get; private set; }

    // --- Collateral (what backs the issue) and its state registration ---

    /// <summary>
    /// Appraised value of the collateral, in <see cref="Currency"/>. Distinct from
    /// <see cref="TotalValue"/>: that is what the issue is offered at, this is what an appraiser
    /// certified the backing asset is worth. Null until an appraisal is recorded.
    /// </summary>
    public decimal? CollateralValue { get; private set; }

    /// <summary>Date of the appraisal <see cref="CollateralValue"/> comes from.</summary>
    public DateTime? CollateralValuedAtUtc { get; private set; }

    /// <summary>Appraiser who certified the value.</summary>
    public string? CollateralAppraiser { get; private set; }

    /// <summary>
    /// Registration number of the encumbrance recorded against the asset in the state register — the
    /// evidence that the collateral is actually pledged and not merely declared.
    /// </summary>
    public string? EncumbranceRegistrationNumber { get; private set; }

    /// <summary>When the encumbrance was registered.</summary>
    public DateTime? EncumbranceRegisteredAtUtc { get; private set; }

    /// <summary>State registration number of the issue itself, assigned on registration.</summary>
    public string? IssueRegistrationNumber { get; private set; }

    /// <summary>
    /// The user acting as collateral manager for this issue. Their access is deliberately narrow —
    /// encumbrance status and the holder register, nothing else.
    /// </summary>
    public Guid? CollateralManagerUserId { get; private set; }

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

    private readonly List<PropertyRoom> _rooms = new();

    /// <summary>The unit's room breakdown, in the order the admin entered it.</summary>
    public IReadOnlyCollection<PropertyRoom> Rooms => _rooms.AsReadOnly();

    // private ctor: creation only through the factory method
    private Property() { }

    public static Property Create(
        string name, string? description, string? address, decimal totalValue,
        decimal tokenPrice, long totalTokens, string currency,
        string? propertyType = null, string? city = null, int? yearBuilt = null,
        string? developer = null, int? floors = null, long minPurchaseTokens = TokenAmount.Smallest)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Property name is required.");
        if (totalValue <= 0)
            throw new DomainException("Property total value must be positive.");
        if (tokenPrice <= 0)
            throw new DomainException("Token price must be positive.");
        if (totalTokens <= 0)
            throw new DomainException("Total tokens must be positive.");
        if (minPurchaseTokens < TokenAmount.Smallest)
            throw new DomainException($"Minimum purchase must be at least {TokenAmount.Smallest} token.");
        if (minPurchaseTokens > totalTokens)
            throw new DomainException("Minimum purchase cannot exceed the whole issue.");
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
            MinPurchaseTokens = minPurchaseTokens,
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
    /// Makes this property a unit of <paramref name="buildingId"/>, or detaches it (null) back to a
    /// standalone issue. Only the grouping changes — the issue, its supply and its holders stay put.
    /// </summary>
    public void AssignToBuilding(Guid? buildingId)
        => BuildingId = buildingId == Guid.Empty ? null : buildingId;

    /// <summary>
    /// Records what the unit physically is: kind, number, floor, room count and total area. Only
    /// non-null arguments are applied, so a caller can PATCH a single field. <paramref name="unitType"/>
    /// of <see cref="UnitType.Unspecified"/> is treated as "leave as is".
    /// </summary>
    public void SetUnitDetails(
        UnitType? unitType = null, string? unitNumber = null, int? floorNumber = null,
        int? roomCount = null, decimal? totalAreaSqM = null)
    {
        if (roomCount is < 0)
            throw new DomainException("Room count cannot be negative.");
        if (totalAreaSqM is <= 0)
            throw new DomainException("Unit area must be positive.");

        if (unitType is not null and not UnitType.Unspecified)
            UnitType = unitType.Value;

        UnitNumber = unitNumber ?? UnitNumber;
        FloorNumber = floorNumber ?? FloorNumber;
        RoomCount = roomCount ?? RoomCount;
        TotalAreaSqM = totalAreaSqM ?? TotalAreaSqM;
    }

    /// <summary>
    /// Replaces the whole room breakdown with <paramref name="rooms"/> (name + area, in order). The
    /// list is replaced wholesale rather than patched row by row: the admin edits it as one table,
    /// and an empty list clears it. The sum of the rooms is NOT forced to equal
    /// <see cref="TotalAreaSqM"/> — plans legitimately disagree with the sellable area — so callers
    /// that want to flag a discrepancy compare the two themselves.
    /// </summary>
    public void ReplaceRooms(IEnumerable<(string Name, decimal AreaSqM)> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);

        var replacement = rooms
            .Select((r, i) => PropertyRoom.Create(Id, r.Name, r.AreaSqM, i))
            .ToList();

        _rooms.Clear();
        _rooms.AddRange(replacement);
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

    /// <summary>
    /// Annuls part of the issue that was never placed (draft Decree, ch. 11): the shares disappear
    /// from the offering and the issue shrinks to match. Only unsold capacity can be annulled this
    /// way — shares already in an investor's hands are withdrawn from circulation instead, which is
    /// what invalidating an issue does.
    /// </summary>
    public void AnnulUnplacedTokens(long count)
    {
        if (count <= 0)
            throw new DomainException("Annulled token count must be positive.");
        if (count > AvailableTokens)
            throw new DomainException("Cannot annul more tokens than remain unplaced.");

        AvailableTokens -= count;
        TotalTokens -= count;
    }

    /// <summary>
    /// Declares the issue invalid (draft Decree, §73). Terminal: sales stop, nothing more can be
    /// placed, and the shares in circulation are to be withdrawn and the money returned. The
    /// withdrawal and the refunds are carried out by the application layer — the aggregate only
    /// records that the issue is no longer valid.
    /// </summary>
    public void Invalidate()
    {
        Status = PropertyStateFactory.Create(Status).Invalidate(this).Status;
        SalesPaused = true;
    }

    /// <summary>
    /// Records the appraisal behind the issue: value, date and appraiser. All three go together — a
    /// value without a date and an appraiser is not evidence of anything.
    /// </summary>
    public void SetCollateralAppraisal(decimal value, DateTime valuedAtUtc, string appraiser)
    {
        if (value <= 0)
            throw new DomainException("Collateral value must be positive.");
        if (string.IsNullOrWhiteSpace(appraiser))
            throw new DomainException("Appraiser is required.");

        CollateralValue = value;
        CollateralValuedAtUtc = valuedAtUtc;
        CollateralAppraiser = appraiser;
    }

    /// <summary>Records the encumbrance registered against the asset in the state register.</summary>
    public void SetEncumbrance(string registrationNumber, DateTime registeredAtUtc)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
            throw new DomainException("Encumbrance registration number is required.");

        EncumbranceRegistrationNumber = registrationNumber;
        EncumbranceRegisteredAtUtc = registeredAtUtc;
    }

    /// <summary>Records the state registration number assigned to the issue.</summary>
    public void SetIssueRegistrationNumber(string registrationNumber)
    {
        if (string.IsNullOrWhiteSpace(registrationNumber))
            throw new DomainException("Issue registration number is required.");

        IssueRegistrationNumber = registrationNumber;
    }

    /// <summary>Assigns (or clears, with null) the collateral manager responsible for this issue.</summary>
    public void SetCollateralManager(Guid? userId)
        => CollateralManagerUserId = userId == Guid.Empty ? null : userId;

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
