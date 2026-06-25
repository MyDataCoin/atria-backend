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
            .Must(KyrgyzPhone.IsValid)
            .WithMessage($"Phone must be a valid Kyrgyzstan number, e.g. {KyrgyzPhone.Example}.");
    }
}
