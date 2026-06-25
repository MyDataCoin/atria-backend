using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// Manual bank-transfer settings. The "session" is just static bank details + a
/// payment reference; a back-office webhook (HMAC-signed) confirms settlement.
/// </summary>
public sealed class BankTransferOptions
{
    public const string SectionName = "BankTransfer";

    /// <summary>HMAC secret used to verify the back-office confirmation webhook. Secret.</summary>
    [Required]
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Beneficiary account name shown to the investor.</summary>
    [Required]
    public string BeneficiaryName { get; set; } = string.Empty;

    /// <summary>IBAN / account number shown to the investor.</summary>
    [Required]
    public string Iban { get; set; } = string.Empty;

    /// <summary>BIC / SWIFT code shown to the investor.</summary>
    [Required]
    public string Bic { get; set; } = string.Empty;

    /// <summary>Bank name shown to the investor.</summary>
    public string? BankName { get; set; }

    /// <summary>Max age (seconds) of the confirmation webhook timestamp (replay protection).</summary>
    [Range(1, 3600)]
    public int WebhookToleranceSeconds { get; set; } = 300;
}
