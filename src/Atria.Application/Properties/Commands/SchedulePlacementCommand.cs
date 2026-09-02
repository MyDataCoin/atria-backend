using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Sets the placement window and the sum the offering is trying to raise. Admin only. Only the
/// supplied fields change, so a client can PATCH one of them.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="OpensAtUtc">When the placement should open; <c>null</c> to leave unchanged.</param>
/// <param name="ClosesAtUtc">When it should close; <c>null</c> to leave unchanged.</param>
/// <param name="TargetAmount">The sum to raise by the closing date; <c>null</c> to leave unchanged.</param>
/// <param name="OfferedAreaSqM">
/// The area being placed, in m², when the issue covers only part of the object; <c>null</c> to leave
/// unchanged. Refused once shares have been placed — it is what a share stands for.
/// </param>
public sealed record SchedulePlacementCommand(
    Guid PropertyId,
    DateTime? OpensAtUtc,
    DateTime? ClosesAtUtc,
    decimal? TargetAmount,
    decimal? OfferedAreaSqM = null) : IRequest<Result>;

/// <summary>
/// Writes the window onto the issue. Setting it is all this does — the dates are acted on by the
/// placement sweep, not here, so scheduling an offering and running it stay separable.
/// </summary>
public sealed class SchedulePlacementCommandHandler : IRequestHandler<SchedulePlacementCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public SchedulePlacementCommandHandler(
        IPropertyRepository properties,
        ICurrentUserService currentUser,
        IAuditWriter audit,
        IUnitOfWork uow)
    {
        _properties = properties;
        _currentUser = currentUser;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(SchedulePlacementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("property.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(Error.Forbidden("property.forbidden", "Only administrators can schedule a placement."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        try
        {
            property.SchedulePlacement(
                request.OpensAtUtc, request.ClosesAtUtc, request.TargetAmount, request.OfferedAreaSqM);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("placement.invalidWindow", ex.Message));
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PlacementScheduled,
            $"Размещение объекта «{property.Name}»: "
            + $"открытие {Format(property.PlacementOpensAtUtc)}, закрытие {Format(property.PlacementClosesAtUtc)}, "
            + $"цель {(property.TargetAmount is { } t ? $"{t:0.##} {property.Currency}" : "не задана")}"
            + (property.OfferedAreaSqM is { } a ? $", выпускается {a:0.##} м²" : string.Empty),
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static string Format(DateTime? value)
        => value is { } d ? d.ToString("yyyy-MM-dd HH:mm 'UTC'") : "не задано";
}
