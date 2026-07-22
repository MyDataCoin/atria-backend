using Atria.Application.Appeals.Commands;
using Atria.Domain.Appeals;
using FluentValidation;

namespace Atria.Application.Appeals.Validators;

/// <summary>Validates <see cref="SubmitAppealCommand"/> — the message is required and bounded.</summary>
public sealed class SubmitAppealCommandValidator : AbstractValidator<SubmitAppealCommand>
{
    public SubmitAppealCommandValidator()
    {
        RuleFor(x => x.Message).NotEmpty().MaximumLength(Appeal.MaxMessageLength);
        RuleFor(x => x.Username).MaximumLength(64);
    }
}
