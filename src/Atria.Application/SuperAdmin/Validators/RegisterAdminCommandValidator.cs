using Atria.Application.Common;
using Atria.Application.SuperAdmin.Commands;
using FluentValidation;

namespace Atria.Application.SuperAdmin.Validators;

/// <summary>
/// Validates <see cref="RegisterAdminCommand"/>: username and full name are required, and the
/// one-time password already has to satisfy the same strength policy the account will be held to
/// when it changes it on first sign-in.
/// </summary>
public sealed class RegisterAdminCommandValidator : AbstractValidator<RegisterAdminCommand>
{
    public RegisterAdminCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).ApplyPasswordPolicy();
    }
}
