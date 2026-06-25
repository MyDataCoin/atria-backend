using Asp.Versioning;
using Atria.Api.Controllers.Common;
using Atria.Api.Controllers.Requests;
using Atria.Application.Abstractions;
using Atria.Application.Documents.Commands;
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
    [HttpPost]
    [Authorize(Roles = "Investor")]
    [Consumes("multipart/form-data")]
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
    [HttpGet("me")]
    [Authorize(Roles = "Investor")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var result = await Sender.Send(new GetMyDocumentsQuery(), ct);
        return ToActionResult(result);
    }

    /// <summary>Downloads a single document (owner, Admin, or Compliance). Returns the raw file stream.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Investor,Admin,Compliance")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var result = await Sender.Send(new GetDocumentByIdQuery(id), ct);
        if (result.IsFailure)
            return ToActionResult(result);

        var doc = result.Value;
        return File(doc.Content, doc.ContentType, doc.FileName);
    }
}
