using Atria.Domain.Holders;

namespace Atria.Application.Abstractions;

/// <summary>
/// Aggregate repository for <see cref="HolderPosition"/>, the current-state holder projection keyed by
/// (property, wallet).
/// </summary>
public interface IHolderPositionRepository : IRepository<HolderPosition>
{
    /// <summary>
    /// The position of a wallet in an issuance, or null if the address holds nothing there yet. Tracked
    /// so the caller can adjust it and persist through the unit of work.
    /// </summary>
    Task<HolderPosition?> GetByAddressAsync(Guid propertyId, string walletAddress, CancellationToken ct);

    /// <summary>All holding positions of an issuance, read-only. The basis for a snapshot / reporting.</summary>
    Task<IReadOnlyList<HolderPosition>> GetByPropertyAsync(Guid propertyId, CancellationToken ct);
}
