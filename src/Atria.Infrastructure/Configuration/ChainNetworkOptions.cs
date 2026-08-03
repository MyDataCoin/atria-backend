using System.ComponentModel.DataAnnotations;

namespace Atria.Infrastructure.Configuration;

/// <summary>One configured network, keyed in settings by the tag stored on the issue.</summary>
public sealed class ChainNetworkOptions
{
    /// <summary>EIP-155 chain id (97 = BNB testnet, 56 = BNB mainnet).</summary>
    [Range(1, long.MaxValue)]
    public long ChainId { get; init; }

    /// <summary>JSON-RPC endpoint.</summary>
    [Required]
    [Url]
    public string RpcUrl { get; init; } = null!;

    /// <summary>Identity registry deployed on this network. Shared by every issue on it.</summary>
    [Required]
    public string IdentityRegistryAddress { get; init; } = null!;

    /// <summary>Allowlist deployed on this network. Shared by every issue on it.</summary>
    [Required]
    public string AllowlistAddress { get; init; } = null!;

    /// <summary>
    /// Root of the public block explorer for this network, e.g. <c>https://testnet.bscscan.com</c>.
    /// </summary>
    /// <remarks>
    /// Configured rather than built into the frontend: which explorer serves a chain is a property of
    /// the chain, and a dashboard that hardcodes one sends the holder to the wrong network's page the
    /// day the issue moves to mainnet. Optional — without it the record is still shown, just without
    /// the outbound links.
    /// </remarks>
    [Url]
    public string? ExplorerBaseUrl { get; init; }
}
