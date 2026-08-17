using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Application.Properties.Dtos;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>Creates a property. Restricted to Admins.</summary>
public sealed class CreatePropertyCommandHandler
    : IRequestHandler<CreatePropertyCommand, Result<Guid>>
{
    private readonly IPropertyRepository _properties;
    private readonly IBuildingRepository _buildings;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CreatePropertyCommandHandler(
        IPropertyRepository properties,
        IBuildingRepository buildings,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _properties = properties;
        _buildings = buildings;
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

        // A unit must land in a building that exists — otherwise the FK would fail deep in SaveChanges
        // with an opaque 500 instead of a 404 the admin form can show.
        if (request.BuildingId is { } buildingId && await _buildings.GetByIdAsync(buildingId, ct) is null)
            return Result.Failure<Guid>(Error.NotFound("building.notFound", "Building not found."));

        var property = Property.Create(
            request.Name, request.Description, request.Address,
            request.TotalValue, request.TokenPrice, request.TotalTokens, request.Currency,
            request.PropertyType, request.City, request.YearBuilt, request.Developer, request.Floors);

        property.AssignToBuilding(request.BuildingId);
        property.SetUnitDetails(
            PropertyDto.ParseUnitType(request.UnitType), request.UnitNumber, request.FloorNumber,
            request.RoomCount, request.TotalAreaSqM);

        if (request.Rooms is { Count: > 0 })
            property.ReplaceRooms(request.Rooms.Select(r => (r.Name, r.AreaSqM)));

        await _properties.AddAsync(property, ct);

        // Audited in the SAME transaction as the write: the object can never appear without its entry.
        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyCreated,
            $"Создан объект «{property.Name}»", AuditSeverity.Success, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(property.Id);
    }
}
