using Atria.Application.Investments.Commands;
using FluentValidation;

namespace Atria.Application.Investments.Validators;

/// <summary>Validates the shape of an inbound payment webhook (not its signature — the Strategy does that).</summary>
public sealed class HandlePaymentCallbackCommandValidator : AbstractValidator<HandlePaymentCallbackCommand>
{
    public HandlePaymentCallbackCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();
        RuleFor(x => x.Payload).NotNull();
        RuleFor(x => x.Payload.RawBody).NotEmpty().When(x => x.Payload is not null);
    }
}
