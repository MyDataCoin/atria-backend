using Atria.Application.Abstractions;
using Atria.Application.Buildings.Dtos;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Queries;

/// <summary>Lists the buildings with the units registered inside each of them.</summary>
public sealed record GetBuildingsQuery : IRequest<Result<IReadOnlyList<BuildingDto>>>;

/// <summary>
/// Returns the buildings together with their units. Unit visibility follows the same rule as the
/// property catalogue: drafts are staff-only, so a public caller sees a building with only its
/// announced / open / completed units.
/// <para>
/// A building with nothing publicly visible inside it is withheld entirely, not returned empty. A
/// draft is work in progress — leaking the name, address, developer and photos of an object that
/// has not been put on sale yet is the same disclosure a draft property is deliberately protected
/// from, and an empty card on the public site is not a listing anyone wants either.
/// </para>
/// </summary>
public sealed class GetBuildingsQueryHandler
    : IRequestHandler<GetBuildingsQuery, Result<IReadOnlyList<BuildingDto>>>
{
    private readonly IBuildingRepository _buildings;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;

    public GetBuildingsQueryHandler(
        IBuildingRepository buildings, IPropertyRepository properties, ICurrentUserService currentUser)
    {
        _buildings = buildings;
        _properties = properties;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<BuildingDto>>> Handle(GetBuildingsQuery request, CancellationToken ct)
    {
        var buildings = await _buildings.GetAllAsync(ct);

        // One read of the catalogue, grouped in memory: far cheaper than a units query per building.
        var seesDrafts = _currentUser.IsInRole(Role.Admin) || _currentUser.IsInRole(Role.Realtor);
        var unitsByBuilding = (await _properties.GetAllAsync(ct))
            .Where(p => p.BuildingId is not null && (seesDrafts || p.Status != PropertyStatus.Draft))
            .GroupBy(p => p.BuildingId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PropertyDto>)g.Select(PropertyDto.From).ToList());

        IReadOnlyList<BuildingDto> dtos = buildings
            .Select(b => BuildingDto.From(
                b, unitsByBuilding.TryGetValue(b.Id, out var units) ? units : Array.Empty<PropertyDto>()))
            // Staff keep every building, including the ones still being filled in; everyone else
            // only sees a building that actually has something on sale.
            .Where(dto => seesDrafts || dto.Units.Count > 0)
            .ToList();

        return Result.Success(dtos);
    }
}
