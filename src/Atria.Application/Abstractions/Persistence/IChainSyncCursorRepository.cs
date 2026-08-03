using Atria.Domain.Holders;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="ChainSyncCursor"/>, one per issue.</summary>
public interface IChainSyncCursorRepository : IRepository<ChainSyncCursor>
{
    /// <summary>The cursor for an issue, or null when its contract has never been replayed.</summary>
    Task<ChainSyncCursor?> FindByPropertyAsync(Guid propertyId, CancellationToken ct);
}
