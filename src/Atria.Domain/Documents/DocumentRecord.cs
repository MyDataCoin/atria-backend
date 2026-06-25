using Atria.Domain.Common;

namespace Atria.Domain.Documents;

/// <summary>
/// Metadata of an uploaded document (passport, contract, dividend statement, ...).
/// The actual bytes live in object storage; only the storage key is kept here.
/// Immutable after creation — raises no domain events.
/// </summary>
public sealed class DocumentRecord : AggregateRoot
{
    public Guid OwnerUserId { get; private set; }
    public DocumentType Type { get; private set; }
    public string FileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public string StorageKey { get; private set; } = null!;
    public long SizeBytes { get; private set; }

    // private ctor: creation only through the factory method
    private DocumentRecord(
        Guid ownerUserId,
        DocumentType type,
        string fileName,
        string contentType,
        string storageKey,
        long sizeBytes)
    {
        Id = Guid.NewGuid();
        OwnerUserId = ownerUserId;
        Type = type;
        FileName = fileName;
        ContentType = contentType;
        StorageKey = storageKey;
        SizeBytes = sizeBytes;
    }

    public static DocumentRecord Create(
        Guid ownerUserId,
        DocumentType type,
        string fileName,
        string contentType,
        string storageKey,
        long sizeBytes)
        => new(ownerUserId, type, fileName, contentType, storageKey, sizeBytes);
}
