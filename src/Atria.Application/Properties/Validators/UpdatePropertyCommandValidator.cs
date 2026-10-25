using Atria.Application.Properties.Commands;
using FluentValidation;

namespace Atria.Application.Properties.Validators;

/// <summary>
/// Validates a property edit. Only lengths and ranges: every field is optional, and the domain
/// guards the rest. Without this an over-long value reached the database and came back as a 409.
/// </summary>
public sealed class UpdatePropertyCommandValidator : AbstractValidator<UpdatePropertyCommand>
{
    public UpdatePropertyCommandValidator()
    {
        RuleFor(x => x.Name).MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Address).MaximumLength(512);
        RuleFor(x => x.PropertyType).MaximumLength(64);
        RuleFor(x => x.City).MaximumLength(128);
        RuleFor(x => x.Developer).MaximumLength(256);
        RuleFor(x => x.UnitNumber).MaximumLength(32);

        RuleFor(x => x.DocumentedUse).MaximumLength(PropertyFieldLimits.DocumentedUse);
        RuleFor(x => x.BuildingClass).MaximumLength(PropertyFieldLimits.BuildingClass);
        RuleFor(x => x.WallMaterial).MaximumLength(PropertyFieldLimits.WallMaterial);
        RuleFor(x => x.Heating).MaximumLength(PropertyFieldLimits.Heating);
        RuleFor(x => x.Elevator).MaximumLength(PropertyFieldLimits.Elevator);
        RuleFor(x => x.Security).MaximumLength(PropertyFieldLimits.Security);
        RuleFor(x => x.Parking).MaximumLength(PropertyFieldLimits.Parking);
        RuleFor(x => x.ManagementCompany).MaximumLength(PropertyFieldLimits.ManagementCompany);
        RuleFor(x => x.LocationDescription).MaximumLength(PropertyFieldLimits.LocationDescription);
        RuleFor(x => x.LandPlotCode).MaximumLength(PropertyFieldLimits.CadastreCode);
        RuleFor(x => x.CadastralNumber).MaximumLength(PropertyFieldLimits.CadastreCode);
        RuleFor(x => x.TotalValue).GreaterThan(0).When(x => x.TotalValue is not null);
        RuleFor(x => x.TokenPrice).GreaterThan(0).When(x => x.TokenPrice is not null);
        RuleFor(x => x.TotalTokens).GreaterThan(0).When(x => x.TotalTokens is not null);
        RuleFor(x => x.MinPurchaseTokens).GreaterThanOrEqualTo(1).When(x => x.MinPurchaseTokens is not null);
        RuleFor(x => x.ReadinessPercent).InclusiveBetween(0, 100).When(x => x.ReadinessPercent is not null);

        RuleForEach(x => x.Rooms).ChildRules(room =>
        {
            room.RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
            room.RuleFor(r => r.AreaSqM).GreaterThan(0);
        });
    }
}
