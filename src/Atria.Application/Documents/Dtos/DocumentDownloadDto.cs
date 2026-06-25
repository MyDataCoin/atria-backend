namespace Atria.Application.Documents.Dtos;

/// <summary>Download payload: the document stream plus the headers the API needs to serve it.</summary>
/// <param name="Content">Readable stream of the document's raw bytes.</param>
/// <param name="FileName">File name to serve the download as.</param>
/// <param name="ContentType">MIME content type used for the response.</param>
public sealed record DocumentDownloadDto(
    Stream Content,
    string FileName,
    string ContentType);
