using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;

namespace Atria.Application.Properties.Queries;

/// <summary>Returns one property by id. Properties are part of the public catalogue.</summary>
public sealed class GetPropertyByIdQueryHandler
    : IRequestHandler<GetPropertyByIdQuery, Result<PropertyDto>>
{
    private readonly IPropertyRepository _properties;

    public GetPropertyByIdQueryHandler(IPropertyRepository properties) => _properties = properties;

    public async Task<Result<PropertyDto>> Handle(GetPropertyByIdQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure<PropertyDto>(
                Error.NotFound("property.notFound", "Property not found."));

        var dto = new PropertyDto(
            property.Id, property.Name, property.Description, property.TokenPrice,
            property.AvailableTokens, property.TotalTokens, property.Currency, property.IsActive);

        return Result.Success(dto);
    }
}
