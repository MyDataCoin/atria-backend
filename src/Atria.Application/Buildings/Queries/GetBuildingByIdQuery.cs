using Atria.Application.Abstractions;
using Atria.Application.Buildings.Dtos;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Queries;

/// <summary>
/// Fetches one building with the units registered inside it. A building with nothing publicly
/// visible inside is reported as not found to non-staff — exactly as a draft property is, so its
/// existence is not leaked by a direct link either.
/// </summary>
public sealed record GetBuildingByIdQuery(Guid Id) : IRequest<Result<BuildingDto>>;

public sealed class GetBuildingByIdQueryHandler
    : IRequestHandler<GetBuildingByIdQuery, Result<BuildingDto>>
{
    private readonly IBuildingRepository _buildings;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;

    public GetBuildingByIdQueryHandler(
        IBuildingRepository buildings, IPropertyRepository properties, ICurrentUserService currentUser)
    {
        _buildings = buildings;
        _properties = properties;
        _currentUser = currentUser;
    }

    public async Task<Result<BuildingDto>> Handle(GetBuildingByIdQuery request, CancellationToken ct)
    {
        var building = await _buildings.GetByIdAsync(request.Id, ct);
        if (building is null)
            return Result.Failure<BuildingDto>(Error.NotFound("building.notFound", "Building not found."));

        // Draft units are staff-only, exactly as in the property catalogue.
        var seesDrafts = _currentUser.IsInRole(Role.Admin) || _currentUser.IsInRole(Role.Realtor);
        IReadOnlyList<PropertyDto> units = (await _properties.GetByBuildingAsync(building.Id, ct))
            .Where(p => seesDrafts || p.Status != PropertyStatus.Draft)
            .Select(PropertyDto.From)
            .ToList();

        if (units.Count == 0 && !seesDrafts)
            return Result.Failure<BuildingDto>(Error.NotFound("building.notFound", "Building not found."));

        return Result.Success(BuildingDto.From(building, units));
    }
}
