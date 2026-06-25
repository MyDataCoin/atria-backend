using Amazon.S3;
using Amazon.S3.Model;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Storage;

/// <summary>
/// <see cref="IDocumentStorage"/> over AWS S3 (or any S3-compatible store). Objects are
/// stored under a GUID-prefixed key so file names never collide; only the opaque key is
/// persisted on the DocumentRecord. Bytes never touch the database.
/// </summary>
public sealed class S3DocumentStorageAdapter : IDocumentStorage
{
    private readonly IAmazonS3 _s3;
    private readonly S3Options _options;
    private readonly ILogger<S3DocumentStorageAdapter> _logger;

    public S3DocumentStorageAdapter(
        IAmazonS3 s3,
        IOptions<S3Options> options,
        ILogger<S3DocumentStorageAdapter> logger)
    {
        _s3 = s3;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> SaveAsync(
        Stream content, string fileName, string contentType, CancellationToken ct)
    {
        // GUID prefix guarantees uniqueness; original file name kept as a suffix for traceability.
        var key = $"{Guid.NewGuid():N}/{SanitizeFileName(fileName)}";

        var request = new PutObjectRequest
        {
            BucketName = _options.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false
        };

        await _s3.PutObjectAsync(request, ct);

        _logger.LogInformation(
            "Document stored in S3. Bucket={Bucket} Key={Key}", _options.BucketName, key);

        return key;
    }

    public async Task<Stream> GetAsync(string storageKey, CancellationToken ct)
    {
        var request = new GetObjectRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey
        };

        var response = await _s3.GetObjectAsync(request, ct);
        // Caller owns disposing the returned stream (it also closes the underlying response).
        return response.ResponseStream;
    }

    // Strips path separators so the original name can't escape the GUID prefix.
    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(name) ? "file" : name;
    }
}
