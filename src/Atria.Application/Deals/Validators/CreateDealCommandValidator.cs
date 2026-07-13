using Atria.Application.Deals.Commands;
using FluentValidation;

namespace Atria.Application.Deals.Validators;

/// <summary>Validates <see cref="CreateDealCommand"/> inputs.</summary>
public sealed class CreateDealCommandValidator : AbstractValidator<CreateDealCommand>
{
    public CreateDealCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.CommissionPercent).InclusiveBetween(0m, 100m);
    }
}
