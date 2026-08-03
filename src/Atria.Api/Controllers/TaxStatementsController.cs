using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Application.Abstractions;
using Atria.Application.Tax.Commands;
using Atria.Application.Tax.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>
/// Income statements an investor hands to the tax office. Issued and rendered by the server: a
/// document assembled in the browser carries no evidence of anything, because its number is whatever
/// the page invented and nobody can check afterwards that its figures are the platform's figures.
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/tax-statements")]
[Authorize(Roles = "Investor")]
public sealed class TaxStatementsController : ApiControllerBase
{
    public TaxStatementsController(ISender sender) : base(sender) { }

    /// <summary>Issues the caller's statement for a calendar year.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and approved KYC — the name on a tax document has to be the
    /// verified one. Built from the investor's own holdings. One statement per year: asking again
    /// returns the one already issued rather than minting a second document with different figures
    /// making the same claim. 409 when verification is not approved or there is nothing to report.
    /// </remarks>
    /// <param name="year">The year the statement covers.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("{year:int}")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Issue(int year, CancellationToken ct)
    {
        var result = await Sender.Send(new IssueTaxStatementCommand(year), ct);
        return ToCreatedResult(result, nameof(Mine), null);
    }

    /// <summary>The caller's issued statements, newest year first.</summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [ProducesResponseType<IReadOnlyList<TaxStatementDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Mine(CancellationToken ct)
        => ToActionResult(await Sender.Send(new GetMyTaxStatementsQuery(), ct));

    /// <summary>Downloads one of the caller's statements as a printable document.</summary>
    /// <remarks>
    /// The bytes are rendered server-side, so what is handed over is what the platform issued; the
    /// browser only prints it. Someone else's statement answers 404 — whether a given id exists is
    /// not the caller's business.
    /// </remarks>
    /// <param name="id">Statement identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}/document")]
    [Produces("text/html", "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Document(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new RenderTaxStatementQuery(id), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var file = result.Value;
        return File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary>Verifies a statement by the code printed on it. Open to anyone holding the document.</summary>
    /// <remarks>
    /// No authentication: whoever was handed the statement — a tax officer, a bank — must be able to
    /// check it. The code is unguessable and the lookup is by code alone, so possession of the
    /// document is what grants the check and there is nothing to enumerate.
    /// </remarks>
    /// <param name="code">The verification code from the document.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    [ProducesResponseType<TaxStatementVerificationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Verify(string code, CancellationToken ct)
        => ToActionResult(await Sender.Send(new VerifyTaxStatementQuery(code), ct));
}
