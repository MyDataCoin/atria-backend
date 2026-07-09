using Atria.Application.Support.Commands;
using Atria.Domain.Support;
using FluentValidation;

namespace Atria.Application.Support.Validators;

/// <summary>Validates <see cref="CreateTicketCommand"/> inputs.</summary>
public sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(SupportTicket.MaxSubjectLength);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8000);
    }
}
