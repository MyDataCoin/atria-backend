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
    /// Blocks that must sit on top of a transaction before it counts as settled, its own included.
    /// </summary>
    /// <remarks>
    /// There is deliberately no switch to skip this check. The setting it replaces marked every
    /// submitted operation confirmed without asking the chain anything, which meant the investor saw
    /// a settled allocation and the registry counted shares that may never have landed. A single
    /// confirmation can still be undone by a reorg, so the default is the depth BNB Chain treats as
    /// final.
    /// </remarks>
    [Range(1, 200)]
    public int ConfirmationsRequired { get; init; } = 15;

    /// <summary>Worker poll interval, in seconds.</summary>
    public int PollSeconds { get; init; } = 10;

    /// <summary>Max operations processed per pass.</summary>
    public int BatchSize { get; init; } = 20;

    /// <summary>Max submission attempts before an operation is left in Failed.</summary>
    public int MaxAttempts { get; init; } = 5;
}
