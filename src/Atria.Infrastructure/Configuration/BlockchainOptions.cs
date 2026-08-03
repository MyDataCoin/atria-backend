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

    /// <summary>
    /// Configured networks, keyed by the tag stored in <c>Property.TokenChain</c>.
    /// </summary>
    /// <remarks>
    /// There is deliberately no global chain id and no global token contract address here. The token
    /// contract belongs to the issue (<c>Property.TokenContractAddress</c>): a single address in
    /// configuration works until the second issue exists, after which every operation quietly targets
    /// whichever contract the config file happens to name. What is genuinely shared per network — the
    /// RPC endpoint, the identity registry, the allowlist — lives here.
    /// </remarks>
    public Dictionary<string, ChainNetworkOptions> Networks { get; init; } = new();

    /// <summary>
    /// When true, the reconciliation pass marks any Submitted operation as confirmed WITHOUT asking
    /// the chain whether it was finalized. Off by default: reporting an operation as confirmed when
    /// nothing has been verified on chain is a lie the rest of the system then builds on — the
    /// investor sees a settled allocation, the registry counts shares that may never have landed.
    /// Leave it off; a real finality check replaces it.
    /// </summary>
    public bool AutoConfirmSubmitted { get; init; }

    /// <summary>Worker poll interval, in seconds.</summary>
    public int PollSeconds { get; init; } = 10;

    /// <summary>Max operations processed per pass.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Max submission attempts before an operation is left in Failed.</summary>
    public int MaxAttempts { get; init; } = 5;
}
