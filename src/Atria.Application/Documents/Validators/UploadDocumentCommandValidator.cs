using Atria.Application.Documents.Commands;
using FluentValidation;

namespace Atria.Application.Documents.Validators;

/// <summary>Validates an upload: non-empty file, sane size, and an allowed content type.</summary>
public sealed class UploadDocumentCommandValidator : AbstractValidator<UploadDocumentCommand>
{
    // 10 MB upper bound for KYC/contract documents.
    private const long MaxSizeBytes = 10 * 1024 * 1024;

    private static readonly string[] AllowedContentTypes =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/jpg"
    ];

    public UploadDocumentCommandValidator()
    {
        RuleFor(x => x.Content)
            .NotNull().WithMessage("File content is required.")
            .Must(s => s.CanRead).WithMessage("File content must be readable.")
            // Only seekable streams expose a length to bound; reject empty ones.
            .Must(s => !s.CanSeek || (s.Length > 0 && s.Length <= MaxSizeBytes))
            .WithMessage($"File size must be between 1 byte and {MaxSizeBytes} bytes.");

        RuleFor(x => x.FileName)
            .NotEmpty().WithMessage("File name is required.")
            .MaximumLength(255);

        RuleFor(x => x.ContentType)
            .NotEmpty().WithMessage("Content type is required.")
            .Must(ct => AllowedContentTypes.Contains(ct.ToLowerInvariant()))
            .WithMessage("Unsupported content type. Allowed: PDF, PNG, JPEG.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Unknown document type.");
    }
}
