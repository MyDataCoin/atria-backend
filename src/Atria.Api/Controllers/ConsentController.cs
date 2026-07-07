using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Consents.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Legal consent: records an investor's acceptance of a consent document version.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/consent")]
[Authorize]
public sealed class ConsentController : ApiControllerBase
{
    public ConsentController(ISender sender) : base(sender) { }

    /// <summary>Records the current investor's acceptance of a consent document.</summary>
    /// <remarks>
    /// Requires a valid bearer token. Stores WHO (the authenticated user), WHAT (<c>Type</c> +
    /// <c>Version</c>) and WHEN (server UTC) as regulator evidence that the user accepted a specific
    /// version of the text. <c>Accepted</c> must be <c>true</c>. Idempotent: re-posting the same
    /// type+version returns the existing acceptance instead of creating a duplicate. Accepting the
    /// personal-data notice (<c>Pdn</c>) of the current version is a precondition for <c>POST /kyc/submit</c>.
    /// </remarks>
    /// <param name="request">The consent type (by name), the accepted version, and the acceptance flag.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Record(RecordConsentRequest request, CancellationToken ct)
    {
        var result = await Sender.Send(
            new RecordConsentCommand(request.Type, request.Version, request.Accepted), ct);
        return ToActionResult(result);
    }
}
