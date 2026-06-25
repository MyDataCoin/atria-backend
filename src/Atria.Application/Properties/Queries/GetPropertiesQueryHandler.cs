using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;

namespace Atria.Application.Properties.Queries;

/// <summary>Returns the full property catalogue as read models.</summary>
public sealed class GetPropertiesQueryHandler
    : IRequestHandler<GetPropertiesQuery, Result<IReadOnlyList<PropertyDto>>>
{
    private readonly IPropertyRepository _properties;

    public GetPropertiesQueryHandler(IPropertyRepository properties) => _properties = properties;

    public async Task<Result<IReadOnlyList<PropertyDto>>> Handle(GetPropertiesQuery request, CancellationToken ct)
    {
        var properties = await _properties.GetAllAsync(ct);

        IReadOnlyList<PropertyDto> dtos = properties
            .Select(p => new PropertyDto(
                p.Id, p.Name, p.Description, p.TokenPrice,
                p.AvailableTokens, p.TotalTokens, p.Currency, p.IsActive))
            .ToList();

        return Result.Success(dtos);
    }
}
