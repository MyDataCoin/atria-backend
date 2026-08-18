using Atria.Domain.Whitelist;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="WhitelistEntry"/> — the queue of purchase requests waiting to
/// be minted.
/// </summary>
public interface IWhitelistEntryRepository : IRepository<WhitelistEntry>
{
    /// <summary>The queued request for a purchase, or null. Keeps the event handlers idempotent.</summary>
    Task<WhitelistEntry?> GetByInvestmentAsync(Guid investmentId, CancellationToken ct);

    /// <summary>
    /// The operator's queue, newest request first, optionally narrowed to one issuance or one status.
    /// Joined to the issuance's name and the holding batch's number so the table reads without a
    /// second round trip. Read-only.
    /// </summary>
    Task<IReadOnlyList<(WhitelistEntry Entry, string? PropertyName, string? MintListNumber)>>
        ListForOperatorAsync(Guid? propertyId, WhitelistStatus? status, int take, CancellationToken ct);

    /// <summary>The requests behind these ids, tracked, for placing into a mint list.</summary>
    Task<IReadOnlyList<WhitelistEntry>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>The requests a mint list is holding, tracked, so the batch can move them together.</summary>
    Task<IReadOnlyList<WhitelistEntry>> ListByMintListAsync(Guid mintListId, CancellationToken ct);
}
