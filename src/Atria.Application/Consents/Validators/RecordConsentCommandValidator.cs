using Atria.Application.Consents.Commands;
using FluentValidation;

namespace Atria.Application.Consents.Validators;

/// <summary>Validates <see cref="RecordConsentCommand"/> inputs.</summary>
public sealed class RecordConsentCommandValidator : AbstractValidator<RecordConsentCommand>
{
    public RecordConsentCommandValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Version).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Accepted).Equal(true).WithMessage("Consent must be accepted.");
    }
}
