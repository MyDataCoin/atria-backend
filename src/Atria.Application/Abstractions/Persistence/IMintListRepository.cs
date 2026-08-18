using Atria.Domain.Whitelist;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="MintList"/>, the batches handed to the exchange.</summary>
public interface IMintListRepository : IRepository<MintList>
{
    /// <summary>A batch with all of its lines loaded, or null. Read-only.</summary>
    Task<MintList?> GetWithItemsAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Batch headers, newest first, optionally narrowed to one issuance, joined to that issuance's
    /// name. Lines are not loaded. Read-only.
    /// </summary>
    Task<IReadOnlyList<(MintList List, string? PropertyName)>> ListAsync(Guid? propertyId, CancellationToken ct);

    /// <summary>
    /// How many batches were created in a calendar year. Feeds the sequential batch number; the unique
    /// index on the number is what actually guarantees no two batches share one.
    /// </summary>
    Task<int> CountForYearAsync(int year, CancellationToken ct);
}
