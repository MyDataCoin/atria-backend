using Atria.Application.Kyc.Commands;
using FluentValidation;

namespace Atria.Application.Kyc.Validators;

/// <summary>Validates <see cref="ReviewKycCommand"/>: a rejection must carry a reason.</summary>
public sealed class ReviewKycCommandValidator : AbstractValidator<ReviewKycCommand>
{
    public ReviewKycCommandValidator()
    {
        RuleFor(x => x.KycId).NotEmpty();

        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(1024)
            .When(x => !x.Approve);
    }
}
