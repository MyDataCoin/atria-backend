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
            .Must(PhoneNumbers.IsValid)
            .WithMessage($"Phone must be a valid number: a Kyrgyz number or any number in international form (+country code), e.g. {PhoneNumbers.Example}.");

        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches(@"^\d{4,8}$")
            .WithMessage("Code must be 4 to 8 digits.");
    }
}
