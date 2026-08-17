using Atria.Application.Buildings.Commands;
using FluentValidation;

namespace Atria.Application.Buildings.Validators;

/// <summary>Validates building creation input (mirrors the domain invariants, fails fast).</summary>
public sealed class CreateBuildingCommandValidator : AbstractValidator<CreateBuildingCommand>
{
    public CreateBuildingCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Address).MaximumLength(512);
        RuleFor(x => x.City).MaximumLength(128);
        RuleFor(x => x.Developer).MaximumLength(256);
        RuleFor(x => x.BuildingType).MaximumLength(64);
        RuleFor(x => x.YearBuilt).InclusiveBetween(1800, 2100).When(x => x.YearBuilt is not null);
        RuleFor(x => x.Floors).InclusiveBetween(1, 500).When(x => x.Floors is not null);
    }
}
