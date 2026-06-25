using Atria.Application.Auth.Commands;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates that a refresh token was supplied.</summary>
public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .MaximumLength(512);
    }
}
