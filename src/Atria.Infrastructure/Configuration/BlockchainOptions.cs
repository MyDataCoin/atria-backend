using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>
/// On-chain integration settings. The signer URL points at an EXTERNAL custody
/// service (KMS/HSM); NO private key ever lives in this process or in config.
/// </summary>
public sealed class BlockchainOptions
{
    public const string SectionName = "Blockchain";

    /// <summary>Base URL of the external signer/custody service.</summary>
    [Required]
    [Url]
    public string SignerUrl { get; init; } = null!;

    /// <summary>EVM chain id of the permissioned BEP-20 token network.</summary>
    [Required]
    public string ChainId { get; init; } = null!;

    /// <summary>Address of the permissioned token contract (allowlist target).</summary>
    [Required]
    public string TokenContractAddress { get; init; } = null!;

    /// <summary>Network used to anchor attestation Merkle roots (Solana for the pilot).</summary>
    [Required]
    public string AnchorNetwork { get; init; } = null!;

    /// <summary>
    /// When true (default for the pilot), the reconciliation pass auto-confirms any
    /// Submitted operation without querying the chain for finality. Set false once a
    /// real chain/IChainAnchor finality check is wired in.
    /// </summary>
    public bool AutoConfirmSubmitted { get; init; } = true;

    /// <summary>Worker poll interval, in seconds.</summary>
    public int PollSeconds { get; init; } = 10;

    /// <summary>Max operations processed per pass.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Max submission attempts before an operation is left in Failed.</summary>
    public int MaxAttempts { get; init; } = 5;
}
