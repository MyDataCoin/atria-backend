using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>Publishes a property's offering (flips it to active / open). Restricted to Admins.</summary>
public sealed class PublishPropertyCommandHandler
    : IRequestHandler<PublishPropertyCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public PublishPropertyCommandHandler(
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(PublishPropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(
                Error.Forbidden("property.forbidden", "Only administrators can publish properties."));

        var property = await _properties.GetByIdAsync(request.Id, ct);
        if (property is null)
            return Result.Failure(
                Error.NotFound("property.notFound", "Property not found."));

        if (property.Status is not (PropertyStatus.Draft or PropertyStatus.ComingSoon))
            return Result.Failure(
                Error.Conflict("property.not_publishable", "Only a draft or coming-soon property can be published."));

        property.Publish();
        _properties.Update(property);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyPublished,
            $"Объект «{property.Name}» опубликован и открыт для инвестиций",
            AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
