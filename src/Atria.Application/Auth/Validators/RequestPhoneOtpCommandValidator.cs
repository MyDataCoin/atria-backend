using Atria.Application.Auth.Commands;
using Atria.Domain.Users;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates that the phone is a Kyrgyzstan (+996) mobile number.</summary>
public sealed class RequestPhoneOtpCommandValidator : AbstractValidator<RequestPhoneOtpCommand>
{
    public RequestPhoneOtpCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Must(PhoneNumbers.IsValid)
            .WithMessage($"Phone must be a valid number: a Kyrgyz number or any number in international form (+country code), e.g. {PhoneNumbers.Example}.");
    }
}
