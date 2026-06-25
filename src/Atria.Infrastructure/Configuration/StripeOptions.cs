using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Stripe settings. Bound from configuration, validated on start.
/// Both values are secrets and must come from a secret store.
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Stripe";

    /// <summary>Stripe secret API key (sk_live_… / sk_test_…). Secret.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (whsec_…) used to verify Stripe-Signature. Secret.</summary>
    [Required]
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Default settlement currency (ISO 4217, lower-case for Stripe).</summary>
    public string DefaultCurrency { get; set; } = "usd";

    /// <summary>Tolerance (seconds) for the Stripe-Signature timestamp (replay protection).</summary>
    [Range(1, 3600)]
    public long WebhookToleranceSeconds { get; set; } = 300;
}
