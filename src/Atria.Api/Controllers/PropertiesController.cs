using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Investments.Dtos;
using Atria.Application.Investments.Queries;
using Atria.Application.Properties.Commands;
using Atria.Application.Properties.Dtos;
using Atria.Application.Properties.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Tokenized property catalogue. Browsing is open; creation is Admin only.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/properties")]
public sealed class PropertiesController : ApiControllerBase
{
    public PropertiesController(ISender sender) : base(sender) { }

    /// <summary>Lists all properties in the catalogue (anonymous or authenticated).</summary>
    /// <remarks>
    /// Returns the full public catalogue of tokenized properties, including each property's current token
    /// price and remaining supply. Open to everyone; no authentication required.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<PropertyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await Sender.Send(new GetPropertiesQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Fetches a single property by id.</summary>
    /// <remarks>
    /// Returns one property from the public catalogue by its id. Open to everyone; no authentication
    /// required. Responds with 404 when the property does not exist.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PropertyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetPropertyByIdQuery(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Lists the property's Active investors and their token holdings. Admin / Compliance.</summary>
    /// <remarks>
    /// Requires the <c>Admin</c> or <c>Compliance</c> role. Returns one row per investor with an Active
    /// investment in the property: their verified KYC full name (decrypted server-side) and total tokens
    /// held (Σ amount / token price). The ownership share percent is not returned — compute it on the client
    /// as <c>tokens / totalTokens * 100</c>. Investors without a KYC profile appear with a null name.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">The property's Active investors with token holdings.</response>
    /// <response code="401">The request is not authenticated.</response>
    /// <response code="403">The caller is not in the Admin or Compliance role.</response>
    [HttpGet("{id:guid}/investments")]
    [Authorize(Roles = "Admin,Compliance")]
    [ProducesResponseType<IReadOnlyList<PropertyInvestorDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInvestors(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetPropertyInvestorsQuery(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Creates a new tokenized property. Admin only.</summary>
    /// <remarks>
    /// Registers a new property in the catalogue and returns its id. Requires the <b>Admin</b> role.
    /// <c>TotalValue</c>, <c>TokenPrice</c>, and <c>TotalTokens</c> must all be positive and <c>Currency</c>
    /// must be <c>KGS</c> — the platform issues in Kyrgyzstani som only.
    /// <para>
    /// To add a unit to a building, send <c>buildingId</c> plus the unit fields: <c>unitType</c>
    /// (<c>apartment</c> | <c>garage</c> | <c>parking_space</c> | <c>commercial</c> | <c>storage</c> |
    /// <c>other</c>), <c>unitNumber</c>, <c>floorNumber</c>, <c>roomCount</c>, <c>totalAreaSqM</c> and the
    /// <c>rooms</c> breakdown (<c>[{ "name": "Кухня+Столовая", "areaSqM": 28.68 }, …]</c>). Each unit is
    /// its own token issue — <c>tokenPrice</c> and <c>totalTokens</c> are per unit, not per building.
    /// Omit <c>buildingId</c> for a standalone issue. 404 when the building does not exist.
    /// </para>
    /// </remarks>
    /// <param name="request">The property details to register.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreatePropertyRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreatePropertyCommand(
            request.Name, request.Description, request.Address, request.TotalValue,
            request.TokenPrice, request.TotalTokens, request.Currency, request.MinPurchaseTokens,
            request.PropertyType, request.City, request.YearBuilt, request.Developer, request.Floors,
            request.BuildingId, request.UnitType, request.UnitNumber, request.FloorNumber,
            request.RoomCount, request.Section, request.Row, request.Spot,
            request.TotalAreaSqM, request.Rooms), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Edits a property's details. Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. Only the supplied fields are changed, so a client can PATCH a
    /// single field. Economics (total value, token price, supply, currency) and the lifecycle status
    /// are <b>not</b> editable here — changing them after investors have bought in would rewrite the
    /// terms of a live offering. The unit fields (building, type, number, floor, area) and the
    /// <c>rooms</c> breakdown are editable; sending <c>rooms</c> replaces the whole list, <c>[]</c> clears
    /// it and omitting it leaves it alone. <c>buildingId</c> moves the unit into another building; send
    /// the all-zero Guid to pull it out into a standalone issue. The parking address
    /// (<c>section</c>, <c>row</c>, <c>spot</c>) is the exception to "only the supplied fields change":
    /// <c>null</c> there CLEARS the value, so switching a unit away from a garage / parking space
    /// wipes an address that no longer applies instead of leaving it stuck on the record.
    /// Responds with 404 when the property does not exist. The edit is
    /// recorded in the audit journal as <c>PropertyUpdated</c>.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdatePropertyRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new UpdatePropertyCommand(
            id, request.Name, request.Description, request.Address, request.PropertyType,
            request.City, request.YearBuilt, request.Developer, request.Floors,
            request.BuildingId, request.UnitType, request.UnitNumber, request.FloorNumber,
            request.RoomCount, request.Section, request.Row, request.Spot,
            request.TotalAreaSqM, request.Rooms), ct));

    /// <summary>Annuls part of an issue that was never placed. Admin only.</summary>
    /// <remarks>
    /// Chapter 11 of the draft Decree. The issue shrinks by the annulled amount and, once it lives on
    /// chain, the contract cap is lowered to match — the registered size and what the contract will
    /// ever allow stay the same number. Only unplaced capacity can be annulled: shares already in an
    /// investor's hands are withdrawn by invalidating the issue, which comes with refunds. 409 when
    /// more is annulled than remains unplaced.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="request">How many unplaced shares to annul, and why.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/annul-tokens")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AnnulTokens(Guid id, AnnulTokensRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new AnnulUnplacedTokensCommand(id, request.TokenCount, request.Reason), ct));

    /// <summary>Declares an issue invalid. Admin only.</summary>
    /// <remarks>
    /// Paragraph 73 of the draft Decree, and it does all three things at once: the issue is marked
    /// invalid and its sales stop, every holder's shares are queued for withdrawal from circulation,
    /// and the money owed back to each holder is recorded at the price the shares were issued at.
    /// Paying those refunds is a separate step. Terminal — an invalidated issue can never be resumed
    /// or republished.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="request">The ground on which the issue is declared invalid.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/invalidate")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Invalidate(Guid id, InvalidateIssueRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new InvalidateIssueCommand(id, request.Reason), ct));

    /// <summary>Reads the collateral file of an issue. Admin, collateral manager or auditor.</summary>
    /// <remarks>
    /// Kept out of the catalogue DTO on purpose: the appraiser, the encumbrance number and who manages
    /// the collateral are not public information. The <b>Auditor</b> role reaches it read-only.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}/collateral")]
    [Authorize(Roles = "Admin,CollateralManager,Auditor")]
    [ProducesResponseType<PropertyCollateralDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCollateral(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPropertyCollateralQuery(id), ct));

    /// <summary>Records what backs the issue and how it is registered. Admin or collateral manager.</summary>
    /// <remarks>
    /// The collateral file: appraised value with its date and appraiser, the encumbrance registered
    /// against the asset in the state register, the state registration number of the issue, and the
    /// collateral manager responsible for it. Every field is optional so the file can be filled in as
    /// documents arrive — but an appraisal is all-or-nothing: value, date and appraiser go together or
    /// none of them do (400 otherwise). Responds with 404 when the property does not exist.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="request">The collateral fields to record.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:guid}/collateral")]
    [Authorize(Roles = "Admin,CollateralManager")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetCollateral(
        Guid id, SetPropertyCollateralRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new SetPropertyCollateralCommand(
            id, request.CollateralValue, request.CollateralValuedAtUtc, request.CollateralAppraiser,
            request.EncumbranceRegistrationNumber, request.EncumbranceRegisteredAtUtc,
            request.IssueRegistrationNumber, request.CollateralManagerUserId), ct));

    /// <summary>Reads the on-chain binding of an issue. Admin or auditor.</summary>
    /// <remarks>
    /// Which token contract carries the issue's shares, on which network, and the issuer's own wallet —
    /// plus a block-explorer link when the network has one configured. All fields are <c>null</c> while
    /// the issue has no contract deployed against it.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}/token-contract")]
    [Authorize(Roles = "Admin,Auditor")]
    [ProducesResponseType<PropertyTokenContractDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTokenContract(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPropertyTokenContractQuery(id), ct));

    /// <summary>Binds a deployed token contract to an issue. Admin only.</summary>
    /// <remarks>
    /// Records the contract address, the network tag and the issuer wallet. Until this is set, every
    /// chain-facing feature of the issue is inert: mint batches carry no contract, the holder register
    /// has nothing to sync against and collateral is never attested. The network tag must be one of the
    /// configured networks (400 otherwise), and both addresses must be EVM addresses. Re-binding is
    /// allowed while nothing has been issued; once a holder position or a mint batch exists the binding
    /// is final and moving it responds with 409.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="request">The contract, the network and the issuer wallet.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("{id:guid}/token-contract")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetTokenContract(
        Guid id, SetPropertyTokenContractRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new SetPropertyTokenContractCommand(
            id, request.TokenContractAddress, request.TokenChain, request.IssuerWalletAddress,
            request.DeploymentBlock), ct));

    /// <summary>Announces a property as "coming soon". Admin only.</summary>
    /// <remarks>
    /// Moves a <b>draft</b> or <b>open</b> property to <b>coming soon</b> — teasing a new draft on the
    /// public site, or pulling an already-open property back off the market. Requires the <b>Admin</b>
    /// role. Responds with 404 when the property does not exist and 409 when it is already coming soon
    /// or completed.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/announce")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Announce(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new AnnouncePropertyCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Reverses a "coming soon" announcement, hiding the property again. Admin only.</summary>
    /// <remarks>
    /// Moves a <b>coming soon</b> property back to <b>draft</b>, removing it from the public site.
    /// Requires the <b>Admin</b> role. Responds with 404 when the property does not exist and 409
    /// when the property is not currently coming soon.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/unannounce")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Unannounce(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new UnannouncePropertyCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Pauses new purchases for a property. Admin only.</summary>
    /// <remarks>
    /// Freezes buying (sets <c>salesPaused = true</c>) without changing the lifecycle status, so the
    /// public site blocks "buy" and new investments are rejected. Requires the <b>Admin</b> role.
    /// Responds with 404 when the property does not exist and 409 when sales are already paused.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pause(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new PausePropertyCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Resumes purchases for a paused property. Admin only.</summary>
    /// <remarks>
    /// Unfreezes buying (sets <c>salesPaused = false</c>). Requires the <b>Admin</b> role. Responds
    /// with 404 when the property does not exist and 409 when sales are not currently paused.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/resume")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resume(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new ResumePropertyCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Publishes a property's offering, opening it to investors. Admin only.</summary>
    /// <remarks>
    /// Moves a <b>draft</b> or <b>coming soon</b> property to <b>open</b>, so the public site lists it
    /// as open for purchase. Takes effect on this call. Requires the <b>Admin</b> role. Responds with
    /// 404 when the property does not exist and 409 when it is already open or completed.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new PublishPropertyCommand(id), ct));

    /// <summary>Completes a property's offering, closing it. Admin only.</summary>
    /// <remarks>
    /// Moves an <b>open</b> property to <b>completed</b> (its offering is finished). Requires the
    /// <b>Admin</b> role. Responds with 404 when the property does not exist and 409 when the
    /// property is not currently open.
    /// </remarks>
    /// <param name="id">The property's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new CompletePropertyCommand(id), ct);
        return ToActionResult(result);
    }

    /// <summary>Uploads a photo for a property (max 3). Admin only. Returns the image id + public URL.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> with a single <c>file</c> part (JPEG/PNG/WebP, ≤ 10 MB). The file is
    /// stored on the backend under a UUID name and served statically; only its URL is persisted. A property
    /// may hold at most 10 photos (<c>409</c> beyond that).
    /// </remarks>
    /// <param name="id">The property's id.</param>
    /// <param name="file">The image file part.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PropertyImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(
            new AddPropertyImageCommand(id, stream, file.FileName, file.ContentType, file.Length), ct);
        return ToActionResult(result);
    }

    /// <summary>Deletes a property photo. Admin only.</summary>
    /// <param name="id">The property's id.</param>
    /// <param name="imageId">The image's id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RemovePropertyImageCommand(id, imageId), ct));

    /// <summary>Uploads a document for a property. Admin only. Returns the document id + public URL.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> with a single <c>file</c> part (PDF or image, ≤ 25 MB). Stored under a UUID
    /// name and served statically; only its URL + metadata are persisted.
    /// </remarks>
    /// <param name="id">The property's id.</param>
    /// <param name="file">The document file part.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/documents")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PropertyDocumentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(
            new AddPropertyDocumentCommand(id, stream, file.FileName, file.ContentType, file.Length), ct);
        return ToActionResult(result);
    }

    /// <summary>Deletes a property document. Admin only.</summary>
    /// <param name="id">The property's id.</param>
    /// <param name="documentId">The document's id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RemovePropertyDocumentCommand(id, documentId), ct));
}
