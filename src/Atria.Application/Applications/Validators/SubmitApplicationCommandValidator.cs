using Atria.Application.Applications.Commands;
using FluentValidation;

namespace Atria.Application.Applications.Validators;

/// <summary>Validates <see cref="SubmitApplicationCommand"/> inputs.</summary>
public sealed class SubmitApplicationCommandValidator : AbstractValidator<SubmitApplicationCommand>
{
    public SubmitApplicationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
