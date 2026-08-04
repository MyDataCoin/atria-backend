using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Requires a configuration value to decode as at least 32 bytes of base64 — i.e. a real 256-bit
/// key rather than a long passphrase.
/// </summary>
/// <remarks>
/// Length checks measure the wrong thing for signing keys. Thirty-two characters of English is a
/// few dozen bits of entropy and cracks off a wordlist; thirty-two bytes from a CSPRNG does not.
/// The distinction matters here because the value protects token forgery and PII at rest.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class Base256BitKeyAttribute : ValidationAttribute
{
    private const int RequiredBytes = 32;

    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        // Absence is [Required]'s business, not ours.
        if (value is not string s || string.IsNullOrWhiteSpace(s))
            return ValidationResult.Success;

        var name = context.MemberName ?? context.DisplayName;

        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(s, buffer, out var written))
        {
            return new ValidationResult(
                $"{name} must be base64-encoded. Generate one with: openssl rand -base64 {RequiredBytes}",
                new[] { name });
        }

        return written >= RequiredBytes
            ? ValidationResult.Success
            : new ValidationResult(
                $"{name} decodes to {written} bytes; at least {RequiredBytes} are required. " +
                $"Generate one with: openssl rand -base64 {RequiredBytes}",
                new[] { name });
    }
}
