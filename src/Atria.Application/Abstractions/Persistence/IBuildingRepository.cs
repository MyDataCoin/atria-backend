using Atria.Domain.Investments;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Building"/>.</summary>
public interface IBuildingRepository : IRepository<Building>
{
    Task<IReadOnlyList<Building>> GetAllAsync(CancellationToken ct);

    /// <summary>True when at least one property is still registered as a unit of this building.</summary>
    Task<bool> HasUnitsAsync(Guid buildingId, CancellationToken ct);
}
