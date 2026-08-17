using Atria.Application.Buildings.Commands;
using FluentValidation;

namespace Atria.Application.Buildings.Validators;

/// <summary>Validates a building photo upload: allowed image type and size cap.</summary>
public sealed class AddBuildingImageCommandValidator : AbstractValidator<AddBuildingImageCommand>
{
    private const long MaxBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly string[] AllowedTypes =
    {
        "image/jpeg", "image/png", "image/webp"
    };

    public AddBuildingImageCommandValidator()
    {
        RuleFor(x => x.BuildingId).NotEmpty();
        RuleFor(x => x.ContentType)
            .Must(t => AllowedTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage("Only JPEG, PNG or WebP images are allowed.");
        RuleFor(x => x.SizeBytes)
            .GreaterThan(0).WithMessage("The image is empty.")
            .LessThanOrEqualTo(MaxBytes).WithMessage("The image exceeds the 10 MB limit.");
    }
}
