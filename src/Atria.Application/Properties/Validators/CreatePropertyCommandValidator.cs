using Atria.Application.Properties.Commands;
using Atria.Domain.Common;
using Atria.Domain.Investments;
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
        // Not merely "three letters": that check passes TJS as readily as KGS, and a wrong code
        // relabels every amount on the issue instead of being rejected. Money is the single place
        // that decides; this rule exists so the caller gets a 400 rather than a domain exception.
        RuleFor(x => x.Currency)
            .NotEmpty()
            .Must(Money.IsSom)
                .WithMessage($"The platform issues in {Money.Currency} ({Money.Symbol}) only.");

        // A minimum bigger than the issue would make the offering unbuyable, and one below a whole
        // token is not a minimum at all — the token does not divide.
        RuleFor(x => x.MinPurchaseTokens)
            .GreaterThanOrEqualTo(TokenAmount.Smallest)
            .LessThanOrEqualTo(x => x.TotalTokens)
                .WithMessage("Minimum purchase cannot exceed the total issue.");

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

        // Where a garage / parking space sits in the car park. Strings on purpose: a section is "B"
        // as often as "2" and a row is written "12А", so there is no numeric rule to apply — only a
        // length. Each is independently optional; a car park may number spaces without rows.
        RuleFor(x => x.Section).MaximumLength(Property.MaxParkingAddressPart);
        RuleFor(x => x.Row).MaximumLength(Property.MaxParkingAddressPart);
        RuleFor(x => x.Spot).MaximumLength(Property.MaxParkingAddressPart);
        RuleFor(x => x.TotalAreaSqM).GreaterThan(0).When(x => x.TotalAreaSqM is not null);

        RuleForEach(x => x.Rooms).ChildRules(room =>
        {
            room.RuleFor(r => r.Name).NotEmpty().MaximumLength(128);
            room.RuleFor(r => r.AreaSqM).GreaterThan(0);
        });
    }
}
