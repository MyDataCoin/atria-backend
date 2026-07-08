using Atria.Application.Properties.Commands;
using FluentValidation;

namespace Atria.Application.Properties.Validators;

/// <summary>Validates a property photo upload: allowed image type and size cap.</summary>
public sealed class AddPropertyImageCommandValidator : AbstractValidator<AddPropertyImageCommand>
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly string[] AllowedTypes =
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public AddPropertyImageCommandValidator()
    {
        RuleFor(x => x.PropertyId).NotEmpty();
        RuleFor(x => x.ContentType)
            .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only JPEG, PNG or WebP images are allowed.");
        RuleFor(x => x.SizeBytes)
            .GreaterThan(0).WithMessage("The image is empty.")
            .LessThanOrEqualTo(MaxBytes).WithMessage("The image exceeds the 10 MB limit.");
    }
}
