using Atria.Application.Auth.Commands;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates that login input is present and well-formed.</summary>
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MaximumLength(128);
    }
}
