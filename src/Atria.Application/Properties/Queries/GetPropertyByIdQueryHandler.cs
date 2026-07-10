using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Queries;

/// <summary>
/// Returns one property by id. Scoped by role: a draft is admin-only, so it is reported as not
/// found to the public / non-admin callers (its existence is not leaked).
/// </summary>
public sealed class GetPropertyByIdQueryHandler
    : IRequestHandler<GetPropertyByIdQuery, Result<PropertyDto>>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;

    public GetPropertyByIdQueryHandler(IPropertyRepository properties, ICurrentUserService currentUser)
    {
        _properties = properties;
        _currentUser = currentUser;
    }

    public async Task<Result<PropertyDto>> Handle(GetPropertyByIdQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.Id, ct);

        var isAdmin = _currentUser.IsInRole(Role.Admin);
        if (property is null || (!isAdmin && property.Status == PropertyStatus.Draft))
            return Result.Failure<PropertyDto>(
                Error.NotFound("property.notFound", "Property not found."));

        var dto = new PropertyDto(
            property.Id, property.Name, property.Description, property.TokenPrice,
            property.AvailableTokens, property.TotalTokens, property.Currency, PropertyDto.ToWireStatus(property.Status), property.SalesPaused,
            property.Address, property.PropertyType, property.City, property.YearBuilt, property.Developer, property.Floors,
            property.Images.Select(i => new PropertyImageDto(i.Id, i.Url)).ToList(),
            property.Documents.Select(d => new PropertyDocumentDto(d.Id, d.Url, d.FileName, d.ContentType)).ToList());

        return Result.Success(dto);
    }
}
