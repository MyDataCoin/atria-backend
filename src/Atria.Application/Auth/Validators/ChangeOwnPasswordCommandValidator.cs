using Atria.Application.Auth.Commands;
using Atria.Application.Common;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates <see cref="ChangeOwnPasswordCommand"/> against the shared password policy.</summary>
public sealed class ChangeOwnPasswordCommandValidator : AbstractValidator<ChangeOwnPasswordCommand>
{
    public ChangeOwnPasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).ApplyPasswordPolicy();
    }
}
