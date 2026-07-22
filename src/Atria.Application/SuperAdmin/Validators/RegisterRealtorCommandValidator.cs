using Atria.Application.SuperAdmin.Commands;
using FluentValidation;

namespace Atria.Application.SuperAdmin.Validators;

/// <summary>Validates <see cref="RegisterRealtorCommand"/> inputs (required username/password/full name).</summary>
public sealed class RegisterRealtorCommandValidator : AbstractValidator<RegisterRealtorCommand>
{
    public RegisterRealtorCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(256);
    }
}
