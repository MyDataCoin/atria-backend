using Atria.Application.Applications.Commands;
using FluentValidation;

namespace Atria.Application.Applications.Validators;

/// <summary>Validates <see cref="ApproveApplicationCommand"/> inputs.</summary>
public sealed class ApproveApplicationCommandValidator : AbstractValidator<ApproveApplicationCommand>
{
    public ApproveApplicationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
