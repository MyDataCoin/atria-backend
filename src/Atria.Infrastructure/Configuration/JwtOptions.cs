using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>Settings for application JWT issuance (access + refresh tokens).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = string.Empty;

    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 signing key — base64 of 32 random bytes (<c>openssl rand -base64 32</c>).
    /// </summary>
    /// <remarks>
    /// A length rule alone is not a strength rule: "atria-super-secret-signing-key!!" is 32
    /// characters and falls to hashcat mode 16500 off a wordlist, at which point anyone can mint a
    /// SuperAdmin token. Requiring base64 of 32 bytes makes the key 256 bits of actual entropy
    /// rather than a passphrase that happens to be long enough.
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters (256-bit) for HS256.")]
    [Base256BitKey]
    public string SigningKey { get; init; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 30;
}
