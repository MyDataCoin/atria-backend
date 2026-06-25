using Atria.Application.Kyc.Commands;
using FluentValidation;

namespace Atria.Application.Kyc.Validators;

/// <summary>
/// Validates the envelope of <see cref="HandleKycCallbackCommand"/> only. The body
/// itself is verified + parsed by the provider Strategy, never trusted here.
/// </summary>
public sealed class HandleKycCallbackCommandValidator : AbstractValidator<HandleKycCallbackCommand>
{
    public HandleKycCallbackCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
    }
}
