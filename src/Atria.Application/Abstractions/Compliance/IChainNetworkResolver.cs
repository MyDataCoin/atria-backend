namespace Atria.Application.Abstractions;

/// <summary>Everything needed to talk to one network.</summary>
/// <param name="Tag">The value stored in <c>Property.TokenChain</c>, e.g. <c>bsc-testnet</c>.</param>
/// <param name="ChainId">EIP-155 chain id used for replay protection.</param>
/// <param name="RpcUrl">JSON-RPC endpoint.</param>
/// <param name="IdentityRegistryAddress">Shared identity registry deployed on this network.</param>
/// <param name="AllowlistAddress">Shared allowlist deployed on this network.</param>
public sealed record ChainNetwork(
    string Tag, long ChainId, string RpcUrl, string IdentityRegistryAddress, string AllowlistAddress);

/// <summary>
/// Resolves the network an issue lives on from the tag recorded on the issue itself.
/// </summary>
/// <remarks>
/// The network is a property of the issue, not of the application. A single global chain id and
/// token address in configuration works exactly until there is a second issue, at which point every
/// operation silently targets whichever contract happens to be in the config file. Adding a network
/// must be a settings change, so the resolver is the one place that knows what is configured.
/// </remarks>
public interface IChainNetworkResolver
{
    /// <summary>The network for a tag, or null when it is not configured.</summary>
    ChainNetwork? Resolve(string? tokenChain);

    /// <summary>Every configured network.</summary>
    IReadOnlyCollection<ChainNetwork> All { get; }
}
