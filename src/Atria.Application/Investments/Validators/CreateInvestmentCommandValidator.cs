using Atria.Application.Investments.Commands;
using FluentValidation;

namespace Atria.Application.Investments.Validators;

/// <summary>Validates <see cref="CreateInvestmentCommand"/> inputs.</summary>
public sealed class CreateInvestmentCommandValidator : AbstractValidator<CreateInvestmentCommand>
{
    public CreateInvestmentCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m);
    }
}
