using Atria.Domain.Investments;

namespace Atria.Application.Abstractions;

/// <summary>Aggregate repository for <see cref="Property"/>.</summary>
public interface IPropertyRepository : IRepository<Property>
{
    Task<IReadOnlyList<Property>> GetAllAsync(CancellationToken ct);

    /// <summary>The units registered inside a building (apartments, garages), each its own issue.</summary>
    Task<IReadOnlyList<Property>> GetByBuildingAsync(Guid buildingId, CancellationToken ct);

    /// <summary>
    /// Issues whose scheduled placement window has come due as of <paramref name="asOfUtc"/>: drafted
    /// or announced ones due to open, and open ones due to close. Tracked (not read-only) so the
    /// placement sweep can move them and persist through the unit of work.
    /// </summary>
    Task<IReadOnlyList<Property>> GetDuePlacementsAsync(DateTime asOfUtc, int batchSize, CancellationToken ct);
}
