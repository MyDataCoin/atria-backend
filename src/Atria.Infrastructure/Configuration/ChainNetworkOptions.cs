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
}
