namespace Atria.Application.Abstractions;

/// <summary>
/// Stores public media files (property photos/documents) on a static-served location and returns
/// a relative HTTP URL (e.g. <c>/media/properties/{uuid}.jpg</c>) which is persisted on the entity.
/// Bytes never touch the database. Distinct from <see cref="IDocumentStorage"/>, whose objects are
/// private and streamed back through an authenticated endpoint.
/// </summary>
public interface IMediaStorage
{
    /// <summary>
    /// Saves <paramref name="content"/> under a random UUID file name inside the
    /// <paramref name="category"/> folder and returns the public URL to persist.
    /// </summary>
    Task<string> SaveAsync(Stream content, string fileName, string contentType, string category, CancellationToken ct);

    /// <summary>Deletes a file previously saved, by the URL returned from <see cref="SaveAsync"/>. No-op if missing.</summary>
    void Delete(string url);
}
