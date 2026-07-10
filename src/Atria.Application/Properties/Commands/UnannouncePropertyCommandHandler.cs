using Atria.Application.Abstractions;
using Atria.Application.Common;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>Reverses an announcement (ComingSoon -> Draft). Restricted to Admins.</summary>
public sealed class UnannouncePropertyCommandHandler
    : IRequestHandler<UnannouncePropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UnannouncePropertyCommandHandler(
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnannouncePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can unannounce properties."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(
                Error.NotFound("property.notFound", "Property not found."));

        if (property.Status != PropertyStatus.ComingSoon)
            return Result.Failure(
                Error.Conflict("property.not_coming_soon", "Only a coming-soon property can be unannounced."));

        property.Unannounce();
        _properties.Update(property);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
