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
    /// must be a 3-letter ISO code (e.g. <c>USD</c>, <c>KGS</c>).
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
            request.TokenPrice, request.TotalTokens, request.Currency), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

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
    /// Opens the property for purchase from either <b>draft</b> or <b>coming soon</b>. Requires the
    /// <b>Admin</b> role. Responds with 404 when the property does not exist and 409 when the
    /// property is already open or completed.
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
    {
        var result = await Sender.Send(new PublishPropertyCommand(id), ct);
        return ToActionResult(result);
    }

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
    /// may hold at most 3 photos (<c>409</c> beyond that).
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
