using Atria.Application.Publications.Commands;
using Atria.Domain.Publications;
using FluentValidation;

namespace Atria.Application.Publications.Validators;

/// <summary>Validates <see cref="CreatePublicationCommand"/> inputs (the wire type is checked in the handler).</summary>
public sealed class CreatePublicationCommandValidator : AbstractValidator<CreatePublicationCommand>
{
    public CreatePublicationCommandValidator()
    {
        RuleFor(x => x.Type).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(Publication.MaxTitleLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(Publication.MaxBodyLength);
    }
}

/// <summary>Validates <see cref="UpdatePublicationCommand"/>: fields are optional, but not blank when supplied.</summary>
public sealed class UpdatePublicationCommandValidator : AbstractValidator<UpdatePublicationCommand>
{
    public UpdatePublicationCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title)
            .NotEmpty().MaximumLength(Publication.MaxTitleLength)
            .When(x => x.Title is not null);
        RuleFor(x => x.Body)
            .NotEmpty().MaximumLength(Publication.MaxBodyLength)
            .When(x => x.Body is not null);
        RuleFor(x => x.Type).NotEmpty().When(x => x.Type is not null);
    }
}
