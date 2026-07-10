using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Queries;

/// <summary>
/// Returns the property catalogue as read models. Scoped by role: drafts are admin-only and hidden
/// from the public catalogue (anonymous / non-admin callers); admins see every status.
/// </summary>
public sealed class GetPropertiesQueryHandler
    : IRequestHandler<GetPropertiesQuery, Result<IReadOnlyList<PropertyDto>>>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;

    public GetPropertiesQueryHandler(IPropertyRepository properties, ICurrentUserService currentUser)
    {
        _properties = properties;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<PropertyDto>>> Handle(GetPropertiesQuery request, CancellationToken ct)
    {
        var properties = await _properties.GetAllAsync(ct);

        // Drafts are admin-only. The public site (anonymous) and investors see coming_soon / open /
        // completed, but never a draft.
        var isAdmin = _currentUser.IsInRole(Role.Admin);

        IReadOnlyList<PropertyDto> dtos = properties
            .Where(p => isAdmin || p.Status != PropertyStatus.Draft)
            .Select(p => new PropertyDto(
                p.Id, p.Name, p.Description, p.TokenPrice,
                p.AvailableTokens, p.TotalTokens, p.Currency, PropertyDto.ToWireStatus(p.Status),
                p.Images.Select(i => new PropertyImageDto(i.Id, i.Url)).ToList(),
                p.Documents.Select(d => new PropertyDocumentDto(d.Id, d.Url, d.FileName, d.ContentType)).ToList()))
            .ToList();

        return Result.Success(dtos);
    }
}
