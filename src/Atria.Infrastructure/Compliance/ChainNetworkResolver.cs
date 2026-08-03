using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Resolves a network from configuration by the tag recorded on the issue. Only one network is
/// configured today, but the lookup exists from the start: adding a second must be a settings change,
/// not a rewrite of the operations layer.
/// </summary>
public sealed class ChainNetworkResolver : IChainNetworkResolver
{
    private readonly IReadOnlyDictionary<string, ChainNetwork> _networks;

    public ChainNetworkResolver(IOptions<BlockchainOptions> options)
        => _networks = options.Value.Networks.ToDictionary(
            kv => kv.Key,
            kv => new ChainNetwork(
                kv.Key, kv.Value.ChainId, kv.Value.RpcUrl,
                kv.Value.IdentityRegistryAddress, kv.Value.AllowlistAddress),
            StringComparer.OrdinalIgnoreCase);

    public ChainNetwork? Resolve(string? tokenChain)
        => string.IsNullOrWhiteSpace(tokenChain) ? null
            : _networks.TryGetValue(tokenChain, out var network) ? network : null;

    public IReadOnlyCollection<ChainNetwork> All => _networks.Values.ToList();
}
