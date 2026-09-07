using Atria.Application.Abstractions;
using Atria.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Atria.Infrastructure.Compliance;

/// <summary>
/// Answers <see cref="IBurnCapability"/> from the signing configuration — the same condition
/// <see cref="EvmTokenGateway"/> would fail on, read before anything depends on it.
/// </summary>
/// <remarks>
/// Custody always answers yes: the keys live in the external signer, and whether that signer is
/// reachable this second is a question about the network rather than about this configuration. Only
/// the operational-key path can be missing a key outright, and only there does the answer change.
/// </remarks>
public sealed class ConfiguredBurnCapability : IBurnCapability
{
    private readonly TokenSigningOptions _options;

    public ConfiguredBurnCapability(IOptions<TokenSigningOptions> options) => _options = options.Value;

    public bool CanBurn
        => _options.Mode != TokenSigningMode.OperationalKey
           || !string.IsNullOrWhiteSpace(_options.CompliancePrivateKey);
}
