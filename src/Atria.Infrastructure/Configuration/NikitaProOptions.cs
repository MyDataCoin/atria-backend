using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Settings for the Nikita Pro SMS gateway (https://smspro.nikita.kg).
/// Bound from configuration section "NikitaPro"; validated on start.
/// </summary>
public sealed class NikitaProOptions
{
    public const string SectionName = "NikitaPro";

    /// <summary>Account login issued by Nikita Pro.</summary>
    [Required]
    public string Login { get; init; } = null!;

    /// <summary>Registered alphanumeric sender name shown to recipients.</summary>
    [Required]
    public string Sender { get; init; } = null!;

    /// <summary>API key / password (secret).</summary>
    [Required]
    public string ApiKey { get; init; } = null!;

    /// <summary>Gateway base URL, e.g. https://smspro.nikita.kg/api/.</summary>
    [Required]
    [Url]
    public string BaseUrl { get; init; } = null!;
}
