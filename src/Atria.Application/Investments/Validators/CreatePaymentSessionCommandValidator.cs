using Atria.Application.Investments.Commands;
using FluentValidation;

namespace Atria.Application.Investments.Validators;

/// <summary>Validates the create-payment-session input.</summary>
public sealed class CreatePaymentSessionCommandValidator : AbstractValidator<CreatePaymentSessionCommand>
{
    public CreatePaymentSessionCommandValidator()
    {
        RuleFor(x => x.InvestmentId).NotEmpty();
        RuleFor(x => x.Provider).IsInEnum();
    }
}
