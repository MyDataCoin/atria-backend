using Atria.Application.Auth.Commands;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates the phone number format for an OTP request (E.164-ish).</summary>
public sealed class RequestPhoneOtpCommandValidator : AbstractValidator<RequestPhoneOtpCommand>
{
    public RequestPhoneOtpCommandValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Matches(@"^\+?[1-9]\d{6,14}$")
            .WithMessage("Phone must be a valid international number.");
    }
}
