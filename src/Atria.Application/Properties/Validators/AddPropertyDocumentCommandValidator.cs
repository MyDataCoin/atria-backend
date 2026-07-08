using Atria.Application.Properties.Commands;
using FluentValidation;

namespace Atria.Application.Properties.Validators;

/// <summary>Validates a property document upload: allowed type and size cap.</summary>
public sealed class AddPropertyDocumentCommandValidator : AbstractValidator<AddPropertyDocumentCommand>
{
    private const long MaxBytes = 25 * 1024 * 1024; // 25 MB

    private static readonly string[] AllowedTypes =
    {
        "application/pdf", "image/jpeg", "image/png", "image/webp"
    };

    public AddPropertyDocumentCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.ContentType)
            .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only PDF or image documents are allowed.");
        RuleFor(x => x.SizeBytes)
            .GreaterThan(0).WithMessage("The document is empty.")
            .LessThanOrEqualTo(MaxBytes).WithMessage("The document exceeds the 25 MB limit.");
    }
}
