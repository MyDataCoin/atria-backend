using Atria.Application.Abstractions;
using Atria.Application.Common;

namespace Atria.Application.Properties.Queries;

/// <summary>
/// The collateral file of an issue. Kept out of <c>PropertyDto</c> on purpose: the public catalogue
/// has no business carrying the appraiser, the encumbrance number or who manages the collateral.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="Currency">Currency the appraised value is denominated in — the issue's own.</param>
/// <param name="CollateralValue">Appraised value of the collateral.</param>
/// <param name="CollateralValuedAtUtc">Date of that appraisal.</param>
/// <param name="CollateralAppraiser">Who certified it.</param>
/// <param name="EncumbranceRegistrationNumber">Registration number of the encumbrance in the state register.</param>
/// <param name="EncumbranceRegisteredAtUtc">When the encumbrance was registered.</param>
/// <param name="IssueRegistrationNumber">State registration number of the issue itself.</param>
/// <param name="CollateralManagerUserId">The user acting as collateral manager for this issue.</param>
public sealed record PropertyCollateralDto(
    Guid PropertyId,
    string Currency,
    decimal? CollateralValue,
    DateTime? CollateralValuedAtUtc,
    string? CollateralAppraiser,
    string? EncumbranceRegistrationNumber,
    DateTime? EncumbranceRegisteredAtUtc,
    string? IssueRegistrationNumber,
    Guid? CollateralManagerUserId);

/// <summary>Reads the collateral file of an issue.</summary>
/// <param name="PropertyId">The issue.</param>
public sealed record GetPropertyCollateralQuery(Guid PropertyId) : IRequest<Result<PropertyCollateralDto>>;

public sealed class GetPropertyCollateralQueryHandler
    : IRequestHandler<GetPropertyCollateralQuery, Result<PropertyCollateralDto>>
{
    private readonly IPropertyRepository _properties;

    public GetPropertyCollateralQueryHandler(IPropertyRepository properties) => _properties = properties;

    public async Task<Result<PropertyCollateralDto>> Handle(
        GetPropertyCollateralQuery request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure<PropertyCollateralDto>(
                Error.NotFound("property.notFound", "Property not found."));

        return Result.Success(new PropertyCollateralDto(
            property.Id, property.Currency, property.CollateralValue, property.CollateralValuedAtUtc,
            property.CollateralAppraiser, property.EncumbranceRegistrationNumber,
            property.EncumbranceRegisteredAtUtc, property.IssueRegistrationNumber,
            property.CollateralManagerUserId));
    }
}
