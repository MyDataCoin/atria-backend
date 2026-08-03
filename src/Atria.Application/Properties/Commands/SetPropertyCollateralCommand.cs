using Atria.Application.Abstractions;
using Atria.Application.Audit;
using Atria.Application.Common;
using Atria.Domain.Audit;
using Atria.Domain.Common;

namespace Atria.Application.Properties.Commands;

/// <summary>
/// Records what backs an issue and how it is registered. Every field is optional so the collateral
/// file can be filled in as the documents arrive, but an appraisal is all-or-nothing: a value without
/// its date and appraiser is not evidence of anything.
/// </summary>
/// <param name="PropertyId">The issue.</param>
/// <param name="CollateralValue">Appraised value of the collateral, in the issue's currency.</param>
/// <param name="CollateralValuedAtUtc">Date of that appraisal.</param>
/// <param name="CollateralAppraiser">Who certified it.</param>
/// <param name="EncumbranceRegistrationNumber">Registration number of the encumbrance in the state register.</param>
/// <param name="EncumbranceRegisteredAtUtc">When the encumbrance was registered.</param>
/// <param name="IssueRegistrationNumber">State registration number of the issue itself.</param>
/// <param name="CollateralManagerUserId">The user acting as collateral manager for this issue.</param>
public sealed record SetPropertyCollateralCommand(
    Guid PropertyId,
    decimal? CollateralValue,
    DateTime? CollateralValuedAtUtc,
    string? CollateralAppraiser,
    string? EncumbranceRegistrationNumber,
    DateTime? EncumbranceRegisteredAtUtc,
    string? IssueRegistrationNumber,
    Guid? CollateralManagerUserId) : IRequest<Result>;

public sealed class SetPropertyCollateralCommandHandler
    : IRequestHandler<SetPropertyCollateralCommand, Result>
{
    private readonly IPropertyRepository _properties;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public SetPropertyCollateralCommandHandler(
        IPropertyRepository properties, IAuditWriter audit, IUnitOfWork uow)
    {
        _properties = properties;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result> Handle(SetPropertyCollateralCommand request, CancellationToken ct)
    {
        var property = await _properties.GetByIdAsync(request.PropertyId, ct);
        if (property is null)
            return Result.Failure(Error.NotFound("property.notFound", "Property not found."));

        var appraisalParts = new object?[]
        {
            request.CollateralValue, request.CollateralValuedAtUtc, request.CollateralAppraiser
        };
        var givenParts = appraisalParts.Count(p => p is not null && p is not string { Length: 0 });
        if (givenParts is > 0 and < 3)
            return Result.Failure(Error.Validation(
                "property.incompleteAppraisal",
                "An appraisal needs its value, its date and its appraiser together."));

        try
        {
            if (givenParts == 3)
                property.SetCollateralAppraisal(
                    request.CollateralValue!.Value, request.CollateralValuedAtUtc!.Value, request.CollateralAppraiser!);

            if (!string.IsNullOrWhiteSpace(request.EncumbranceRegistrationNumber))
                property.SetEncumbrance(
                    request.EncumbranceRegistrationNumber,
                    request.EncumbranceRegisteredAtUtc ?? DateTime.UtcNow);

            if (!string.IsNullOrWhiteSpace(request.IssueRegistrationNumber))
                property.SetIssueRegistrationNumber(request.IssueRegistrationNumber);

            if (request.CollateralManagerUserId is not null)
                property.SetCollateralManager(request.CollateralManagerUserId);
        }
        catch (DomainException ex)
        {
            return Result.Failure(Error.Validation("property.invalidCollateral", ex.Message));
        }

        _properties.Update(property);

        await _audit.WriteAsync(
            AuditEntities.Property, property.Id, AuditEvents.PropertyUpdated,
            $"Обновлены сведения об обеспечении объекта «{property.Name}»",
            AuditSeverity.Success, ct);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
