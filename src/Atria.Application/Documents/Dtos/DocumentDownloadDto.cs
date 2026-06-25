namespace Atria.Application.Documents.Dtos;

/// <summary>Download payload: the document stream plus the headers the API needs to serve it.</summary>
public sealed record DocumentDownloadDto(
    Stream Content,
    string FileName,
    string ContentType);
