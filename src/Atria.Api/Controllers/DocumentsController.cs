using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Documents.Commands;
using Atria.Application.Documents.Dtos;
using Atria.Application.Documents.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atria.Api.Controllers;

/// <summary>Document upload (multipart), download (owner/Admin/Compliance), and listing.</summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/documents")]
[Authorize]
public sealed class DocumentsController : ApiControllerBase
{
    public DocumentsController(ISender sender) : base(sender) { }

    /// <summary>Uploads a document for the current user via multipart form-data.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. The request is
    /// <c>multipart/form-data</c>: a single <c>File</c> part plus a <c>Type</c> field (the document type,
    /// sent by name). The file is streamed straight into object storage and a metadata record is created
    /// owned by the caller; file size and content type are checked by the validator. On success the new
    /// document's id is returned.
    /// </remarks>
    /// <param name="request">Multipart form carrying the file part and the document type.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost]
    [Authorize(Roles = "Investor")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Upload([FromForm] UploadDocumentRequest request, CancellationToken ct)
    {
        // Stream the uploaded file straight into the command — no buffering of bytes in the controller.
        await using var stream = request.File.OpenReadStream();

        var result = await Sender.Send(new UploadDocumentCommand(
            stream,
            request.File.FileName,
            request.File.ContentType,
            request.Type), ct);

        return ToActionResult(result);
    }

    /// <summary>Lists the current user's documents.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c> role and a valid bearer token. Returns only the document metadata owned
    /// by the authenticated caller (the bytes live in object storage); the list is empty when none exist.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    [ProducesResponseType<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyDocumentsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Downloads a single document (owner, Admin, or Compliance). Returns the raw file stream.</summary>
    /// <remarks>
    /// Requires the <c>Investor</c>, <c>Admin</c>, or <c>Compliance</c> role and a valid bearer token. The
    /// owner may download their own document; Admin and Compliance staff may download any. On success the
    /// raw bytes are streamed back with the stored file name and content type. Anyone else receives 403, and
    /// a missing document yields 404.
    /// </remarks>
    /// <param name="id">Id of the document to download.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Admin,Compliance")]
    [Produces("application/octet-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetDocumentByIdQuery(id), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var doc = result.Value;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }
}
