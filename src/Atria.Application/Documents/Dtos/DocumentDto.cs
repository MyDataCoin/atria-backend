using Atria.Domain.Documents;

namespace Atria.Application.Documents.Dtos;

/// <summary>Read model for a document's metadata (bytes live in object storage).</summary>
public sealed record DocumentDto(
    Guid Id,
    DocumentType Type,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);
