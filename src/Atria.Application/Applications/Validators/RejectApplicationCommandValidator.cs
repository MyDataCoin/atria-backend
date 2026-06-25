using Atria.Application.Applications.Commands;
using FluentValidation;

namespace Atria.Application.Applications.Validators;

/// <summary>Validates <see cref="RejectApplicationCommand"/> inputs.</summary>
public sealed class RejectApplicationCommandValidator : AbstractValidator<RejectApplicationCommand>
{
    public RejectApplicationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}
