using Atria.Application.Support.Commands;
using FluentValidation;

namespace Atria.Application.Support.Validators;

/// <summary>Validates <see cref="AddTicketMessageCommand"/> inputs.</summary>
public sealed class AddTicketMessageCommandValidator : AbstractValidator<AddTicketMessageCommand>
{
    public AddTicketMessageCommandValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
    }
}
