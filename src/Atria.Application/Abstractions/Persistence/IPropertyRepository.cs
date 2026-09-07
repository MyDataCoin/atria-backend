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

    /// <summary>
    /// The issue deployed at a token contract, or null when we run no such contract. Address matching
    /// is case-insensitive, since the same address is legitimately written with and without checksum
    /// casing.
    /// </summary>
    /// <remarks>
    /// This is how an inbound report from the trading operator is tied to an issue: the contract is
    /// something we deployed and can verify, whereas an issue identifier in a request body is just a
    /// claim by the sender.
    /// </remarks>
    Task<Property?> FindByTokenContractAsync(string chain, string tokenContractAddress, CancellationToken ct);
}
