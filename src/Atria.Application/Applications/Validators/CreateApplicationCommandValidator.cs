using Atria.Application.Applications.Commands;
using FluentValidation;

namespace Atria.Application.Applications.Validators;

/// <summary>Validates <see cref="CreateApplicationCommand"/> inputs.</summary>
public sealed class CreateApplicationCommandValidator : AbstractValidator<CreateApplicationCommand>
{
    public CreateApplicationCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0m);
    }
}
