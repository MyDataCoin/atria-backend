using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Investments;
using Atria.Domain.Users;

namespace Atria.Application.Buildings.Commands;

/// <summary>
/// Publishes every unit of a building in one go, so an admin opens the whole object instead of
/// clicking through each apartment and garage. Admin only.
/// </summary>
/// <param name="Id">The building whose units are opened.</param>
public sealed record PublishBuildingCommand(Guid Id) : IRequest<Result<PublishBuildingResult>>;

/// <summary>What the bulk publication actually did.</summary>
/// <param name="Published">Units moved to open by this call.</param>
/// <param name="AlreadyOpen">Units that were already open — left alone, not an error.</param>
/// <param name="Skipped">Units that cannot be published (completed or invalidated), left alone.</param>
public sealed record PublishBuildingResult(int Published, int AlreadyOpen, int Skipped);

/// <summary>
/// Publishes the building's units. Units already open are counted, not rejected: publishing a
/// building the admin has partly opened already must finish the job rather than fail on the first
/// unit that is ahead of the rest. Units past the point of publication (completed, invalidated) are
/// left where they are — reopening a finished issue is not what "publish the building" means.
/// </summary>
public sealed class PublishBuildingCommandHandler
    : IRequestHandler<PublishBuildingCommand, Result<PublishBuildingResult>>
{
    private readonly IBuildingRepository _buildings;
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _unitOfWork;

    public PublishBuildingCommandHandler(
        IBuildingRepository buildings,
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork unitOfWork)
    {
        _buildings = buildings;
        _properties = properties;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PublishBuildingResult>> Handle(
        PublishBuildingCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure<PublishBuildingResult>(
                Error.Unauthorized("building.unauthorized", "Authentication is required."));

        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure<PublishBuildingResult>(
                Error.Forbidden("building.forbidden", "Only administrators can publish buildings."));

        var building = await _buildings.GetByIdAsync(request.Id, ct);
        if (building is null)
            return Result.Failure<PublishBuildingResult>(
                Error.NotFound("building.notFound", "Building not found."));

        // Tracked reads: GetByBuildingAsync is AsNoTracking, so each unit is loaded through the
        // aggregate repository to be saved in this same unit of work.
        var units = await _properties.GetByBuildingAsync(building.Id, ct);
        if (units.Count == 0)
            return Result.Failure<PublishBuildingResult>(Error.Conflict(
                "building.noUnits", "This building has no units to publish."));

        var published = 0;
        var alreadyOpen = 0;
        var skipped = 0;

        foreach (var summary in units)
        {
            var unit = await _properties.GetByIdAsync(summary.Id, ct);
            if (unit is null)
                continue;

            switch (unit.Status)
            {
                case PropertyStatus.Draft or PropertyStatus.ComingSoon:
                    unit.Publish();
                    published++;
                    await _audit.WriteAsync(
                        AuditEntities.Property, unit.Id, AuditEvents.PropertyPublished,
                        $"Помещение «{unit.Name}» опубликовано вместе со зданием «{building.Name}»",
                        AuditSeverity.Success, ct);
                    break;

                case PropertyStatus.Open:
                    alreadyOpen++;
                    break;

                default:
                    skipped++;
                    break;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new PublishBuildingResult(published, alreadyOpen, skipped));
    }
}
