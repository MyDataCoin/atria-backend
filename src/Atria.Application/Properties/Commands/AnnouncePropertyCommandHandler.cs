using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>Announces a property as "coming soon" (Draft or Open -> ComingSoon). Restricted to Admins.</summary>
public sealed class AnnouncePropertyCommandHandler
    : IRequestHandler<AnnouncePropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public AnnouncePropertyCommandHandler(
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(AnnouncePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can announce properties."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(
                Error.NotFound("property.notFound", "Property not found."));

        if (property.Status is not (PropertyStatus.Draft or PropertyStatus.Open))
            return Result.Failure(
                Error.Conflict("property.not_announceable", "Only a draft or open property can be announced as coming soon."));

        property.Announce();
        _properties.Update(property);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
