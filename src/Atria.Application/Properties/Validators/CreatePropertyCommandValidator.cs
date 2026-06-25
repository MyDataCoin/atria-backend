using Atria.Application.Properties.Commands;
using FluentValidation;

namespace Atria.Application.Properties.Validators;

/// <summary>Validates property creation input (mirrors the domain invariants, fails fast with friendly messages).</summary>
public sealed class CreatePropertyCommandValidator : AbstractValidator<CreatePropertyCommand>
{
    public CreatePropertyCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Address).MaximumLength(512);
        RuleFor(x => x.TotalValue).GreaterThan(0);
        RuleFor(x => x.TokenPrice).GreaterThan(0);
        RuleFor(x => x.TotalTokens).GreaterThan(0);
        RuleFor(x => x.Currency).NotEmpty().Length(3);
    }
}
