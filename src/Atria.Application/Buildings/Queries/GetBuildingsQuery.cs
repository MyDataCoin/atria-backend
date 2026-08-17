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
/// Returns every building together with its units. Unit visibility follows the same rule as the
/// property catalogue: drafts are staff-only, so a public caller sees the building with only its
/// announced / open / completed units.
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
            .ToList();

        return Result.Success(dtos);
    }
}
