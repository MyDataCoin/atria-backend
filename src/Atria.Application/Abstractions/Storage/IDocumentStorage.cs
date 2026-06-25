namespace Atria.Application.Abstractions;

/// <summary>
/// Adapter over an object store (S3). Returns an opaque storage key persisted on
/// the DocumentRecord; bytes never sit in the database.
/// </summary>
public interface IDocumentStorage
{
    Task<string> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct);
    Task<Stream> GetAsync(string storageKey, CancellationToken ct);
}
