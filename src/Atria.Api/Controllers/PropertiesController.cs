using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
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
}
