using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Didit hosted-KYC settings. Bound from configuration, validated on start.
/// Secrets (ApiKey, WebhookSecret) come from secret stores — never hard-coded.
/// </summary>
public sealed class DiditOptions
{
    public const string SectionName = "Didit";

    /// <summary>Didit API key (Bearer / x-api-key). Secret.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HMAC secret used to verify inbound webhook signatures. Secret.</summary>
    /// <remarks>
    /// This is the only thing standing between an applicant and approving their own KYC, so a short
    /// or guessable value is not merely untidy. <see cref="SecretsGuard"/> additionally rejects
    /// placeholders that were once committed.
    /// </remarks>
    [Required]
    [MinLength(16, ErrorMessage = "Didit:WebhookSecret must be at least 16 characters.")]
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Didit API base URL (e.g. https://verification.didit.me).</summary>
    [Required]
    [Url]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Workflow/verification feature id configured in the Didit console (optional).</summary>
    public string? WorkflowId { get; set; }

    /// <summary>Max age (seconds) of a webhook timestamp before it is rejected as a replay.</summary>
    [Range(1, 3600)]
    public int WebhookToleranceSeconds { get; set; } = 300;
}
