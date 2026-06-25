using Atria.Application.Auth.Commands;
using Atria.Domain.Users;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates the Kyrgyzstan (+996) phone number and the numeric OTP code.</summary>
public sealed class VerifyPhoneOtpCommandValidator : AbstractValidator<VerifyPhoneOtpCommand>
{
    public VerifyPhoneOtpCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Must(KyrgyzPhone.IsValid)
            .WithMessage($"Phone must be a valid Kyrgyzstan number, e.g. {KyrgyzPhone.Example}.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\d{4,8}$")
            .WithMessage("Code must be 4 to 8 digits.");
    }
}
