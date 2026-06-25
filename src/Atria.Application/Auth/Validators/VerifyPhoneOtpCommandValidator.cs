using Atria.Application.Auth.Commands;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates the phone number and numeric OTP code format.</summary>
public sealed class VerifyPhoneOtpCommandValidator : AbstractValidator<VerifyPhoneOtpCommand>
{
    public VerifyPhoneOtpCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+?[1-9]\d{6,14}$")
            .WithMessage("Phone must be a valid international number.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\d{4,8}$")
            .WithMessage("Code must be 4 to 8 digits.");
    }
}
