using Atria.Domain.Documents;

namespace Atria.Application.Documents.Dtos;

/// <summary>Read model for a document's metadata (bytes live in object storage).</summary>
/// <param name="Id">Unique identifier of the document record.</param>
/// <param name="Type">Kind of document (serialized by name).</param>
/// <param name="FileName">Original file name supplied at upload.</param>
/// <param name="ContentType">MIME content type of the stored file (for example <c>application/pdf</c>).</param>
/// <param name="SizeBytes">Size of the stored file in bytes.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the document was uploaded.</param>
public sealed record DocumentDto(
    Guid Id,
    DocumentType Type,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);
