using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Publications.Commands;
using Atria.Application.Publications.Dtos;
using Atria.Application.Publications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// News feed / disclosures: financial reports, news releases, valuation audits and general platform
/// news. Admins publish and edit; investors and the public site read. Scope is decided server-side
/// from the JWT role — an Admin sees drafts too, everyone else sees published items only.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/publications")]
public sealed class PublicationsController : ApiControllerBase
{
    public PublicationsController(ISender sender) : base(sender) { }

    /// <summary>The news feed, newest first. Anonymous or authenticated.</summary>
    /// <remarks>
    /// Returns one page of publications ordered by <c>publishedAtUtc</c> descending. Investors and
    /// anonymous callers see only <c>published</c> items; an <b>Admin</b> also sees drafts. Filter by
    /// <c>propertyId</c> for one object's items, or <c>generalOnly=true</c> for platform-wide news
    /// (items with no property attached); the two are mutually exclusive. <c>type</c> filters by kind.
    /// <c>page</c> defaults to 1 and <c>pageSize</c> to 20 (capped at 100).
    /// </remarks>
    /// <param name="propertyId">Only items about this property.</param>
    /// <param name="generalOnly">Only general items (no property attached).</param>
    /// <param name="type">Filter by kind (e.g. <c>news_release</c>).</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (max 100).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<PublicationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetFeed(
        [FromQuery] Guid? propertyId,
        [FromQuery] bool generalOnly,
        [FromQuery] string? type,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new GetPublicationsQuery(propertyId, generalOnly, type, page, pageSize), ct));

    /// <summary>Fetches one publication with its full body.</summary>
    /// <remarks>
    /// Open to everyone for <c>published</c> items. A draft is reported as <c>404</c> to anyone but an
    /// <b>Admin</b>, so its existence is not leaked.
    /// </remarks>
    /// <param name="id">The publication's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PublicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetPublicationByIdQuery(id), ct));

    /// <summary>Creates and publishes a news-feed item. Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. The item goes live immediately and its readers are notified:
    /// a general item (no <c>propertyId</c>) notifies every investor, while a property-scoped item
    /// notifies that property's holders. Responds with <c>400</c> when the type is unknown or the
    /// title/body are empty or too long, and <c>404</c> when <c>propertyId</c> is set but no such
    /// property exists.
    /// </remarks>
    /// <param name="request">The item to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<PublicationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(CreatePublicationRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(new CreatePublicationCommand(
            request.Type, request.Title, request.Body, request.PropertyId), ct);
        return ToCreatedResult(result, nameof(GetById),
            new { id = result.IsSuccess ? result.Value.Id : Guid.Empty });
    }

    /// <summary>Edits a publication's copy (title / body / type). Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. Only the supplied fields change — the property link, author and
    /// publication time are immutable, and no new reader notification is sent (this is for fixing a
    /// typo in an already-sent report). Responds with <c>404</c> when the publication does not exist.
    /// </remarks>
    /// <param name="id">The publication's unique identifier.</param>
    /// <param name="request">The fields to change.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPatch("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<PublicationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, UpdatePublicationRequest request, CancellationToken ct)
        => ToActionResult(await Sender.Send(
            new UpdatePublicationCommand(id, request.Type, request.Title, request.Body), ct));

    /// <summary>Takes a publication down. Admin only.</summary>
    /// <remarks>
    /// Requires the <b>Admin</b> role. Removes the item from the feed. Responds with <c>404</c> when
    /// the publication does not exist.
    /// </remarks>
    /// <param name="id">The publication's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        => ToActionResult(await Sender.Send(new DeletePublicationCommand(id), ct));
}
