using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Buildings.Commands;
using Atria.Application.Buildings.Dtos;
using Atria.Application.Buildings.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Buildings — the physical objects that group the units sold inside them. A building carries the
/// shared data (address, developer, year, storeys, photos) and no token supply of its own: the
/// apartments, garages and parking spaces inside it are properties, each with its own token issue,
/// created via <c>POST /properties</c> with a <c>buildingId</c>. Browsing is open; writing is Admin only.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/buildings")]
public sealed class BuildingsController : ApiControllerBase
{
    public BuildingsController(ISender sender) : base(sender) { }

    /// <summary>Lists the buildings with the units registered inside each. Open to everyone.</summary>
    /// <remarks>
    /// Every building comes with its <c>units</c> — the properties sold inside it, each carrying its own
    /// token price and remaining supply. Draft units are staff-only: an anonymous or investor caller
    /// sees the building with only its announced / open / completed units.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<BuildingDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetBuildingsQuery(), ct));

    /// <summary>Fetches one building with its units. Open to everyone.</summary>
    /// <param name="id">The building's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<BuildingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetBuildingByIdQuery(id), ct));

    /// <summary>Registers a building. Admin only.</summary>
    /// <remarks>
    /// Creates the container and returns its id. Nothing is issued here — add the apartments and
    /// garages afterwards with <c>POST /properties</c>, passing this id as <c>buildingId</c>; each of
    /// those carries its own token price and supply.
    /// </remarks>
    /// <param name="request">The building's details.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(CreateBuildingRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreateBuildingCommand(
            request.Name, request.Description, request.Address, request.City,
            request.Developer, request.YearBuilt, request.Floors, request.BuildingType), ct);
        return ToCreatedResult(result, nameof(GetById), new { id = result.IsSuccess ? result.Value : Guid.Empty });
    }

    /// <summary>Edits a building's details. Admin only.</summary>
    /// <remarks>
    /// Only the supplied fields are changed, so a client can PATCH a single field. Recorded in the
    /// audit journal as <c>BuildingUpdated</c>. 404 when the building does not exist.
    /// </remarks>
    /// <param name="id">The building's unique identifier.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdateBuildingRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(new UpdateBuildingCommand(
            id, request.Name, request.Description, request.Address, request.City,
            request.Developer, request.YearBuilt, request.Floors, request.BuildingType), ct));

    /// <summary>Publishes every unit of the building in one go. Admin only.</summary>
    /// <remarks>
    /// Opens all the building's draft and coming-soon units to investors, so the whole object goes
    /// on sale in one action instead of unit by unit. Units already open are counted and left alone
    /// rather than treated as an error; units past publication (completed, invalidated) are skipped.
    /// Returns how many of each. 409 when the building has no units at all.
    /// </remarks>
    /// <param name="id">The building's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<PublishBuildingResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new PublishBuildingCommand(id), ct));

    /// <summary>Deletes an empty building. Admin only.</summary>
    /// <remarks>
    /// Refuses with <c>409</c> while any unit is still registered inside: those units are live token
    /// issues, so they must be moved or deleted deliberately first.
    /// </remarks>
    /// <param name="id">The building's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new DeleteBuildingCommand(id), ct));

    /// <summary>Uploads a photo of the building. Admin only. Returns the image id + public URL.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> with a single <c>file</c> part (JPEG/PNG/WebP, ≤ 10 MB). Stored under a
    /// UUID name and served statically; only the URL is persisted. Max 10 photos (<c>409</c> beyond that).
    /// </remarks>
    /// <param name="id">The building's id.</param>
    /// <param name="file">The image file part.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "Admin")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<BuildingImageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var result = await Sender.Send(
            new AddBuildingImageCommand(id, stream, file.FileName, file.ContentType, file.Length), ct);
        return ToActionResult(result);
    }

    /// <summary>Deletes a building photo. Admin only.</summary>
    /// <param name="id">The building's id.</param>
    /// <param name="imageId">The image's id.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, CancellationToken ct)
        => ToActionResult(await Sender.Send(new RemoveBuildingImageCommand(id, imageId), ct));
}
