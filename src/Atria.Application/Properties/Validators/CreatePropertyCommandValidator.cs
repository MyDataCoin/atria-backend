using Atria.Application.Properties.Commands;
using FluentValidation;

namespace Atria.Application.Properties.Validators;

/// <summary>Validates property creation input (mirrors the domain invariants, fails fast with friendly messages).</summary>
public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    public CreatePropertyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Address).MaximumLength(512);
        RuleFor(x => x.TotalValue).GreaterThan(0);
        RuleFor(x => x.TokenPrice).GreaterThan(0);
        RuleFor(x => x.TotalTokens).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);

        // Optional descriptive characteristics.
        RuleFor(x => x.PropertyType).MaximumLength(64);
        RuleFor(x => x.City).MaximumLength(128);
        RuleFor(x => x.Developer).MaximumLength(256);
        RuleFor(x => x.YearBuilt).InclusiveBetween(1800, 2100).When(x => x.YearBuilt is not null);
        RuleFor(x => x.Floors).InclusiveBetween(1, 500).When(x => x.Floors is not null);

        // Unit-in-a-building fields. All optional: a standalone issue sends none of them.
        RuleFor(x => x.UnitNumber).MaximumLength(32);
        RuleFor(x => x.FloorNumber).InclusiveBetween(-10, 500).When(x => x.FloorNumber is not null);
        RuleFor(x => x.RoomCount).InclusiveBetween(0, 100).When(x => x.RoomCount is not null);
        RuleFor(x => x.TotalAreaSqM).GreaterThan(0).When(x => x.TotalAreaSqM is not null);

        RuleForEach(x => x.Rooms).ChildRules(room =>
        {
            room.RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
            room.RuleFor(r => r.AreaSqM).GreaterThan(0);
        });
    }
}
