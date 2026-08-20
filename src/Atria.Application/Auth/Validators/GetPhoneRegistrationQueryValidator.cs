using Atria.Application.Auth.Queries;
using Atria.Domain.Users;
using FluentValidation;

namespace Atria.Application.Auth.Validators;

/// <summary>Тот же формат номера, что и у запроса кода: мусор до базы не доходит.</summary>
public sealed class GetPhoneRegistrationQueryValidator : AbstractValidator<GetPhoneRegistrationQuery>
{
    public GetPhoneRegistrationQueryValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty()
            .Must(KyrgyzPhone.IsValid)
            .WithMessage($"Phone must be a valid Kyrgyzstan number, e.g. {KyrgyzPhone.Example}.");
    }
}
