using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;
using Atria.Domain.Users;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Pushes a placement's closing date back. Admin only.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="NewClosesAtUtc">The new closing date. Must be later than the current one.</param>
/// <param name="Reason">Why the placement is being extended. Required and journalled.</param>
public sealed record ExtendPlacementCommand(
    Guid PropertyId,
    DateTime NewClosesAtUtc,
    string Reason) : IRequest<Result>;

/// <summary>
/// One half of what the management company chose for an undersubscribed placement ("возврат и
/// продление") — the half that keeps selling. The other half is
/// <see cref="ClosePlacementUnsubscribedCommand"/>, which unwinds it.
/// <para>
/// A reason is required. An extension changes the terms investors bought under, and the count on the
/// issue says only that it happened; the journal is where why it happened lives.
/// </para>
/// </summary>
public sealed class ExtendPlacementCommandHandler : IRequestHandler<ExtendPlacementCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly ICurrentUserService _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public ExtendPlacementCommandHandler(
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

    public async Task<Result> Handle(ExtendPlacementCommand request, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated)
            return Result.Failure(Error.Unauthorized("property.unauthorized", "Authentication is required."));
        if (!_currentUser.IsInRole(Role.Admin))
            return Result.Failure(Error.Forbidden("property.forbidden", "Only administrators can extend a placement."));
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result.Failure(Error.Validation("placement.reasonRequired", "A reason is required."));

        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        try
        {
            property.ExtendPlacement(request.NewClosesAtUtc);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Conflict("placement.cannotExtend", ex.Message));
        }

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PlacementExtended,
            $"Размещение объекта «{property.Name}» продлено до "
            + $"{request.NewClosesAtUtc:yyyy-MM-dd HH:mm 'UTC'} "
            + $"(продление №{property.PlacementExtensionCount}): {request.Reason}",
            AuditSeverity.Warning, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
