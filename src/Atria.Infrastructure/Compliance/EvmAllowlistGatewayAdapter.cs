using System.Collections.Concurrent;
using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TesseraEvm = Tessera.Chains.Evm;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Writes allowlist decisions to the restriction contract through Tessera's
/// <c>EvmAllowlistGateway</c>, one gateway per configured network.
/// </summary>
/// <remarks>
/// <para>
/// The function names on our <c>Allowlist</c> contract — <c>addToAllowlist</c> /
/// <c>removeFromAllowlist</c> — are exactly the gateway's defaults, so nothing needs configuring
/// beyond the address and the key.
/// </para>
/// <para>
/// Gateways are created once per network and cached: each owns a Web3 client and a local nonce
/// tracker, and building a fresh one per call would let two writes pick the same nonce and drop one
/// of them.
/// </para>
/// <para>
/// The signing account is the registry/allowlist agent — the same scoped key the anchor uses. It is
/// an agent on the allowlist and holds no role on any token contract, so a compromise costs
/// allowlist integrity, not the issue.
/// </para>
/// </remarks>
public sealed class EvmAllowlistGatewayAdapter : IAllowlistGateway
{
    private readonly IChainNetworkResolver _networks;
    private readonly EvmAnchorOptions _options;
    private readonly ILogger<EvmAllowlistGatewayAdapter> _logger;
    private readonly ConcurrentDictionary<string, TesseraEvm.EvmAllowlistGateway> _gateways = new();

    public EvmAllowlistGatewayAdapter(
        IChainNetworkResolver networks,
        IOptions<EvmAnchorOptions> options,
        ILogger<EvmAllowlistGatewayAdapter> logger)
    {
        _networks = networks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task AddAsync(string chainTag, string address, CancellationToken ct)
    {
        await GatewayFor(chainTag).AddAsync(address, ct);
        _logger.LogInformation("Allowed {Address} on the {ChainTag} allowlist.", address, chainTag);
    }

    public async Task RemoveAsync(string chainTag, string address, CancellationToken ct)
    {
        await GatewayFor(chainTag).RevokeAsync(address, ct);
        _logger.LogInformation("Removed {Address} from the {ChainTag} allowlist.", address, chainTag);
    }

    private TesseraEvm.EvmAllowlistGateway GatewayFor(string chainTag)
    {
        var network = _networks.Resolve(chainTag)
            ?? throw new InvalidOperationException(
                $"Chain '{chainTag}' is not configured under Blockchain:Networks.");

        return _gateways.GetOrAdd(network.Tag, _ =>
            new TesseraEvm.EvmAllowlistGateway(new TesseraEvm.EvmAllowlistGatewayOptions
            {
                RpcUrl = network.RpcUrl,
                ChainId = network.ChainId,
                ContractAddress = network.AllowlistAddress,
                PrivateKey = _options.AgentPrivateKey,
                UseLegacyGasPricing = _options.UseLegacyGasPricing
            }));
    }
}
