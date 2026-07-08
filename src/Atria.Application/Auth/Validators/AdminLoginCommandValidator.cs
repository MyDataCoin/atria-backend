using Atria.Application.Auth.Commands;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Validates <see cref="AdminLoginCommand"/> inputs.</summary>
public sealed class AdminLoginCommandValidator : AbstractValidator<AdminLoginCommand>
{
    public AdminLoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
