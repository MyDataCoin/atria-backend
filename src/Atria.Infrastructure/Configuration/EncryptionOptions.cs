using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>Settings for at-rest PII encryption (AES-GCM).</summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";

    /// <summary>Base64-encoded 32-byte (256-bit) AES key. Never hard-coded; sourced from a secret manager.</summary>
    [Required]
    public string Key { get; init; } = string.Empty;
}
