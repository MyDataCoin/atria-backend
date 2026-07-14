using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>Creates a property. Restricted to Admins.</summary>
public sealed class CreatePropertyCommandHandler
    : IRequestHandler<CreatePropertyCommand, Result<Guid>>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePropertyCommandHandler(
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

    public async Task<Result<Guid>> Handle(CreatePropertyCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<Guid>(
                Error.Unauthorized("property.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure<Guid>(
                Error.Forbidden("property.forbidden", "Only administrators can create properties."));

        var property = Property.Create(
            request.Name, request.Description, request.Address,
            request.TotalValue, request.TokenPrice, request.TotalTokens, request.Currency,
            request.PropertyType, request.City, request.YearBuilt, request.Developer, request.Floors);

        await _properties.AddAsync(property, ct);

        // Audited in the SAME transaction as the write: the object can never appear without its entry.
        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyCreated,
            $"Создан объект «{property.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(property.Id);
    }
}
